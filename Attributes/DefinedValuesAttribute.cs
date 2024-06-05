using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using TypeReferences;

//TODO: Support for method returning (Str, Obj)[] collection for custom display values
//TODO: Test the assignment of the custom data classes (serialized structs with specific values?)
//TODO: Use the Methods returning enumerable collections
//TODO: Test the methods returning non-serializable objects
//TODO: What if the Value collection is changed? Add warning?
//TODO: Utilize WarningsPool to notify about any issues (or display warning instead of the field?)
//TODO: Refactoring

namespace MyBox
{
	/// <summary>
	/// Create Popup with predefined values for string, int or float property
	/// </summary>
	public class DefinedValuesAttribute : PropertyAttribute
	{
		public readonly object[] ValuesArray;
		public readonly string[] LabelsArray;
		public readonly string GetLabelsAndValuesMethod;
		public readonly string InitializeEvent;
		public readonly string ValueChangedMethod;
		public readonly string ValidationMethod;

		public DefinedValuesAttribute(string initializeEvent = null,
									  string valueChangedMethod = null,
									  string validationMethod = null,
									  params object[] definedValues)
		{
			ValuesArray = definedValues;
			InitializeEvent = initializeEvent;
			ValueChangedMethod = valueChangedMethod;
			ValidationMethod = validationMethod;
		}

		public DefinedValuesAttribute(bool withLabels,
									  string initializeEvent = null,
									  string valueChangedMethod = null,
									  string validationMethod = null,
									  params object[] definedValues)
		{
			var actualLength = definedValues.Length / 2;
			ValuesArray = new object[actualLength];
			LabelsArray = new string[actualLength];
			int actualIndex = 0;
			for (var i = 0; i < definedValues.Length; i++)
			{
				ValuesArray[actualIndex] = definedValues[i];
				LabelsArray[actualIndex] = definedValues[++i].ToString();
				actualIndex++;
			}
			InitializeEvent = initializeEvent;
			ValueChangedMethod = valueChangedMethod;
			ValidationMethod = validationMethod;
		}

		public DefinedValuesAttribute(string getLabelsAndValuesMethod,
									  string initializeEvent = null,
									  string valueChangedMethod = null,
									  string validationMethod = null)
		{
			GetLabelsAndValuesMethod = getLabelsAndValuesMethod;
			InitializeEvent = initializeEvent;
			ValueChangedMethod = valueChangedMethod;
			ValidationMethod = validationMethod;
		}

		public DefinedValuesAttribute(Type enumType,
									  string initializeEvent = null,
									  string valueChangedMethod = null,
									  string validationMethod = null)
		{
			if (!enumType.IsEnum)
				throw new ArgumentException($"{enumType} is not an Enum.");

			var labelValuePairs = LabelValuePair.EnumToLabelValuePairs(enumType);
			LabelsArray = labelValuePairs.Select(pair => pair.Label).ToArray();
			ValuesArray = labelValuePairs.Select(pair => pair.Value).ToArray();
			InitializeEvent = initializeEvent;
			ValueChangedMethod = valueChangedMethod;
			ValidationMethod = validationMethod;
		}
	}

	public class LabelValuePair
	{
		public static LabelValuePair[] EnumToLabelValuePairs(Type enumType)
		{
			if (!enumType.IsEnum)
				throw new ArgumentException($"{enumType} is not an Enum.");

			var labels = Enum.GetNames(enumType);
			var values = Enum.GetValues(enumType).Cast<int>();
			return labels.Zip(values, (label, value) => new LabelValuePair(label, value)).ToArray();
		}
		public readonly object Value;
		public readonly string Label;
		public LabelValuePair(string label, object value)
		{
			try
			{
				Value = new TypeReference((Type)value);
			}
			catch (InvalidCastException)
			{
				Value = value;
			}

			Label = label;
		}
	}
}

#if UNITY_EDITOR
namespace MyBox.Internal
{
	using UnityEditor;
	using EditorTools;
	using System.Collections;
	using System.Collections.Generic;

	[CustomPropertyDrawer(typeof(DefinedValuesAttribute))]
	public class DefinedValuesAttributeDrawer : PropertyDrawer
	{
		private Dictionary<SerializedProperty, DefinedValuesData> _propertyToData = new();

		struct DefinedValuesData
		{
			public object[] Objects;
			public string[] Labels;
			public Type ValueType;
			public bool Initialized;
			public Action ValueChanged;
			public Func<int, object, bool> ValidateSelection;
		}

		private void OnInitializationForced(object sender, EventArgs eventArgs) => _propertyToData.Values.ForEach(data => data.Initialized = false);
		private DefinedValuesData Initialize(SerializedProperty targetProperty, DefinedValuesAttribute defaultValuesAttribute)
		{
			if (!_propertyToData.TryGetValue(targetProperty, out var data))
			{
				data = new DefinedValuesData();
				_propertyToData.Add(targetProperty, data);
			}

			if (data.Initialized) return data;
			data.Initialized = true;

			var targetObject = targetProperty.serializedObject.targetObject;

			var values = defaultValuesAttribute.ValuesArray;
			var labels = defaultValuesAttribute.LabelsArray;
			var getLabelAndValuesMethodName = defaultValuesAttribute.GetLabelsAndValuesMethod;
			var initializeEventname = defaultValuesAttribute.InitializeEvent;
			var valueChangedMethodName = defaultValuesAttribute.ValueChangedMethod;
			var validationMethodName = defaultValuesAttribute.ValidationMethod;

			if (getLabelAndValuesMethodName.NotNullOrEmpty())
			{
				object[] valuesFromMethod = GetValuesAndLabelsFromMethod();
				if (valuesFromMethod.NotNullOrEmpty())
				{
					var valueType = valuesFromMethod.First().GetType();
					if (valueType.IsAssignableFrom(typeof(LabelValuePair)))
					{
						var labelValuePairs = valuesFromMethod.Select(x => (LabelValuePair)x);
						labels = labelValuePairs.Select(x => x.Label).ToArray();
						values = labelValuePairs.Select(x => x.Value).ToArray();
					}
					else values = valuesFromMethod;
				}
				else
				{
					WarningsPool.LogWarning(
						"DefinedValuesAttribute caused: Method " + getLabelAndValuesMethodName + " not found or returned null", targetObject);
					return data;
				}
			}

			if (initializeEventname.NotNullOrEmpty())
				SubscribeToInitializeEvent();

			if (valueChangedMethodName.NotNullOrEmpty())
				GetOnValueChangedMethod();

			if (validationMethodName.NotNullOrEmpty())
				GetvalidationMethod();

			var firstValue = values.FirstOrDefault(v => v != null);
			if (firstValue == null) return data;

			data.Objects = values;
			data.ValueType = firstValue.GetType();

			if (labels != null && labels.Length == values.Length) data.Labels = labels;
			else data.Labels = values.Select(v => v?.ToString() ?? "NULL").ToArray();

			return data;

			(MethodInfo, object) GetMethod(string methodName)
			{
				var methodOwner = targetProperty.GetParent();
				if (methodOwner == null) methodOwner = targetObject;
				var type = methodOwner.GetType();
				var bindings = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
				var methodsOnType = type.GetMethods(bindings);
				var method = methodsOnType.SingleOrDefault(m => m.Name == methodName);
				return (method, methodOwner);
			}

			object[] GetValuesAndLabelsFromMethod()
			{
				(var method, var methodOwner) = GetMethod(getLabelAndValuesMethodName);
				if (method == null) return null;

				try
				{
					var result = (IEnumerable)method.Invoke(methodOwner, null);
					List<object> values = new List<object>();
					foreach (var element in result)
						values.Add(element);
					return values.ToArray();
				}
				catch (Exception e)
				{
					WarningsPool.LogWarning(e.ToString());
					return null;
				}
			}

			void GetOnValueChangedMethod()
			{
				(var method, var methodOwner) = GetMethod(valueChangedMethodName);
				if (method == null) return;
				data.ValueChanged += () => method.Invoke(methodOwner, null);
			}

			void GetvalidationMethod()
			{
				(var method, var methodOwner) = GetMethod(validationMethodName);
				if (method == null)
				{
					data.ValidateSelection = (_, _) => true;
					return;
				}

				data.ValidateSelection = (index, value) =>
				{
					try
					{
						return (bool)method.Invoke(methodOwner, new object[] { index, value });
					}
					catch (Exception e)
					{
						WarningsPool.LogWarning(e.ToString());
						return true;
					}
				};
			}

			void SubscribeToInitializeEvent()
			{
				var eventOwner = targetProperty.GetParent() ?? targetObject;
				var type = eventOwner.GetType();
				var eventInfo = type.GetEvent(initializeEventname);
				Delegate handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, this, GetType().GetMethod(nameof(OnInitializationForced), BindingFlags.Instance | BindingFlags.NonPublic));
				eventInfo.AddEventHandler(eventOwner, handler);
			}
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			var data = Initialize(property, (DefinedValuesAttribute)attribute);

			if (data.Labels.IsNullOrEmpty() || data.ValueType != fieldInfo.FieldType)
			{
				EditorGUI.PropertyField(position, property, label);
				return;
			}

			bool isBool = data.ValueType == typeof(bool);
			bool isString = data.ValueType == typeof(string);
			bool isInt = data.ValueType == typeof(int);
			bool isFloat = data.ValueType == typeof(float);
			bool isTypeReference = data.ValueType == typeof(TypeReference);

			EditorGUI.BeginChangeCheck();
			EditorGUI.BeginProperty(position, label, property);
			var newIndex = EditorGUI.Popup(position, label.text, GetSelectedIndex(out var value), data.Labels);
			EditorGUI.EndProperty();

			var currentValue = property.GetValue();
			var valueIsEmpty = currentValue == default || (isString && string.IsNullOrEmpty((string)currentValue));
			if (EditorGUI.EndChangeCheck() || value == null || valueIsEmpty || currentValue != value)
				ApplyNewValue(newIndex);

			int GetSelectedIndex(out object value)
			{
				value = null;
				for (var i = 0; i < data.Objects.Length; i++)
				{
					if (isBool && property.boolValue == Convert.ToBoolean(data.Objects[i])) return i;
					if (isString && property.stringValue == Convert.ToString(data.Objects[i])) return i;
					if (isInt && property.intValue == Convert.ToInt32(data.Objects[i])) return i;
					if (isFloat && Mathf.Approximately(property.floatValue, Convert.ToSingle(data.Objects[i]))) return i;

					if (value == null) value = property.GetValue();
					Func<object, bool> isAtIndex = isTypeReference ?
						v => v.Equals(data.Objects[i]) :
						v => v == data.Objects[i];


					if (isAtIndex.Invoke(value)) return i;
				}

				return 0;
			}

			void ApplyNewValue(int newValueIndex)
			{
				var newValue = data.Objects[newValueIndex];

				if (data.ValidateSelection != null && !data.ValidateSelection(newValueIndex, newValue))
					return;

				if (isBool) property.boolValue = Convert.ToBoolean(newValue);
				else if (isString) property.stringValue = Convert.ToString(newValue);
				else if (isInt) property.intValue = Convert.ToInt32(newValue);
				else if (isFloat) property.floatValue = Convert.ToSingle(newValue);
				else
				{
					property.SetValue(newValue);
					EditorUtility.SetDirty(property.serializedObject.targetObject);
				}

				property.serializedObject.ApplyModifiedProperties();
				data.ValueChanged?.Invoke();
			}
		}
	}
}
#endif