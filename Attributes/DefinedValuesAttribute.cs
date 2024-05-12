using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

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
		public readonly string UseMethod;

		public DefinedValuesAttribute(params object[] definedValues)
		{
			ValuesArray = definedValues;
		}

		public DefinedValuesAttribute(bool withLabels, params object[] definedValues)
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
		}

		public DefinedValuesAttribute(string method)
		{
			UseMethod = method;
		}
	}

	public class LabelValuePair
	{
		public static LabelValuePair[] EnumToLabelValuePairs(Type enumType)
		{
			if (!enumType.IsEnum)
				throw new ArgumentException($"{enumType} is not an Enum.");

			var labels =  Enum.GetNames(enumType);
			var values = Enum.GetValues(enumType).Cast<int>();
			return labels.Zip(values, (label, value) => new LabelValuePair(label, value)).ToArray();
		}
		public readonly object Value;
		public readonly string Label;
		public LabelValuePair(string label, object value)
		{
			Value = value;
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
		private object[] _objects;
		private string[] _labels;
		private Type _valueType;
		private bool _initialized;

		private void Initialize(SerializedProperty targetProperty, DefinedValuesAttribute defaultValuesAttribute)
		{
			if (_initialized) return;
			_initialized = true;
			
			var targetObject = targetProperty.serializedObject.targetObject;
			
			var values = defaultValuesAttribute.ValuesArray;
			var labels = defaultValuesAttribute.LabelsArray;
			var methodName = defaultValuesAttribute.UseMethod;

			if (methodName.NotNullOrEmpty())
			{
				object[] valuesFromMethod = GetValuesAndLabelsFromMethod();
				if (valuesFromMethod.NotNullOrEmpty())
                {
					var valueType = valuesFromMethod.First().GetType();
					if (valueType.IsAssignableFrom(typeof(LabelValuePair)))
					{
						var labelValuePairs = valuesFromMethod.Select(x => (LabelValuePair) x);
						labels = labelValuePairs.Select(x => x.Label).ToArray();
						values = labelValuePairs.Select(x => x.Value).ToArray();
					}
                    else values = valuesFromMethod;
                }
                else
				{
					WarningsPool.LogWarning(
						"DefinedValuesAttribute caused: Method " + methodName + " not found or returned null", targetObject);
					return;
				}
			}

			var firstValue = values.FirstOrDefault(v => v != null);
			if (firstValue == null) return;

			_objects = values;
			_valueType = firstValue.GetType();
			
			if (labels != null && labels.Length == values.Length) _labels = labels;
			else _labels = values.Select(v => v?.ToString() ?? "NULL").ToArray();

			
			object[] GetValuesAndLabelsFromMethod()
			{
				var methodOwner = targetProperty.GetParent();
				if (methodOwner == null) methodOwner = targetObject;
				var type = methodOwner.GetType();
				var bindings = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
				var methodsOnType = type.GetMethods(bindings);
				var method = methodsOnType.SingleOrDefault(m => m.Name == methodName);
				if (method == null) return null;

				try
				{
					var result = (IEnumerable)method.Invoke(methodOwner, null);
					List<object> values = new List<object>();
					foreach (var element in result)
						values.Add(element);
					return values.ToArray();
				}
				catch
				{
					return null;
				}
			}
		}
		
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			Initialize(property, (MyBox2.DefinedValuesAttribute)attribute);
			
			if (_labels.IsNullOrEmpty() || _valueType != fieldInfo.FieldType)
			{
				EditorGUI.PropertyField(position, property, label);
				return;
			}
			
			bool isBool = _valueType == typeof(bool);
			bool isString = _valueType == typeof(string);
			bool isInt = _valueType == typeof(int);
			bool isFloat = _valueType == typeof(float);

			EditorGUI.BeginChangeCheck();
			EditorGUI.BeginProperty(position, label, property);
			var newIndex = EditorGUI.Popup(position, label.text, GetSelectedIndex(), _labels);
			EditorGUI.EndProperty();
			if (EditorGUI.EndChangeCheck()) ApplyNewValue(newIndex);


			int GetSelectedIndex()
			{
				object value = null;
				for (var i = 0; i < _objects.Length; i++)
				{
					if (isBool && property.boolValue == Convert.ToBoolean(_objects[i])) return i;
					if (isString && property.stringValue == Convert.ToString(_objects[i])) return i;
					if (isInt && property.intValue == Convert.ToInt32(_objects[i])) return i;
					if (isFloat && Mathf.Approximately(property.floatValue, Convert.ToSingle(_objects[i]))) return i;

					if (value == null) value = property.GetValue();
					if (value == _objects[i]) return i;
				}

				return 0;
			}

			void ApplyNewValue(int newValueIndex)
			{
				var newValue = _objects[newValueIndex];
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
			}
		}
	}
}
#endif