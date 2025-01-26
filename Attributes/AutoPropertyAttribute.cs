using System;
using UnityEngine;

#if UNITY_EDITOR
using SolidUtilities.Editor;
#endif

namespace MyBox
{
	/// <summary>
	/// Automatically assign components to this Property.
	/// It searches for components from this GO or its children by default.
	/// Pass in an <c>AutoPropertyMode</c> to override this behaviour.
	/// <para></para>
	/// Advanced usage: Filter found objects with a method. To do that, create a 
	/// static method or member method of the current class with the same method
	/// signature as a Func&lt;UnityEngine.Object, bool&gt;. Your predicate method
	/// can be private.
	/// If your predicate method is a member method of the current class, pass in
	/// the nameof that method as the second argument.
	/// If your predicate method is a static method, pass in the typeof class that
	/// contains said method as the third argument.
	/// </summary>
	[AttributeUsage(AttributeTargets.Field)]
	public class AutoPropertyAttribute : PropertyAttribute
	{
		public readonly AutoPropertyMode Mode;
		public readonly string PredicateMethodName;
		public readonly Type PredicateMethodTarget;
  		public readonly bool AllowEmpty;

		public AutoPropertyAttribute(AutoPropertyMode mode = AutoPropertyMode.Children,
			string predicateMethodName = null,
			Type predicateMethodTarget = null,
   			bool allowEmpty = false)
		{
			Mode = mode;
			PredicateMethodTarget = predicateMethodTarget;
			PredicateMethodName = predicateMethodName;
   			AllowEmpty = allowEmpty;
		}
	}

	public enum AutoPropertyMode
	{
		/// <summary>
		/// Search for Components from this GO or its children.
		/// </summary>
		Children = 0,
		/// <summary>
		/// Search for Components from this GO or its parents.
		/// </summary>
		Parent = 1,
		/// <summary>
		/// Search for Components from this GO's current scene.
		/// </summary>
		Scene = 2,
		/// <summary>
		/// Search for Objects from this project's asset folder.
		/// </summary>
		Asset = 3,
		/// <summary>
		/// Search for Objects from anywhere in the project.
		/// Combines the results of Scene and Asset modes.
		/// </summary>
		Any = 4
	}
}

#if UNITY_EDITOR
namespace MyBox.Internal
{
	using UnityEditor;
	using EditorTools;
#if UNITY_2021_2_OR_NEWER
	using UnityEditor.SceneManagement;
#else
	using UnityEditor.Experimental.SceneManagement;
#endif
	using Object = UnityEngine.Object;
	using System.Collections.Generic;
	using System.Linq;

	[CustomPropertyDrawer(typeof(AutoPropertyAttribute))]
	public class AutoPropertyDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			GUI.enabled = false;
			FillProperty(property);
			EditorGUI.PropertyField(position, property, label);
			GUI.enabled = true;
		}

		private readonly Dictionary<AutoPropertyMode, Func<SerializedProperty, Func<Object, bool>, Object[]>> ObjectsGetters
			= new Dictionary<AutoPropertyMode, Func<SerializedProperty, SerializedObject, Func<Object, bool>, Object[]>>
			{
				[AutoPropertyMode.Children] = (property, context, pred) =>
                {
					var self = context.As<Component>();
					var type = property.GetObjectType();
                    var objects = self?.GetComponentsInChildren(type, true)
                                        .Where(pred).ToList();
					objects?.AddRange(self?.GetComponents(type));
					return objects?.ToArray();
                },
				[AutoPropertyMode.Parent] = (property, pred) => property.serializedObject.Context.As<Component>()
					?.GetComponentsInParent(property.Field.FieldType.GetElementType() ?? property.Field.FieldType, true)
					.Where(pred).ToArray(),
				[AutoPropertyMode.Scene] = (property, pred) => MyEditor
					.GetAllComponentsInSceneOf(property.Context,
						property.Field.FieldType.GetElementType() ?? property.Field.FieldType)
					.Where(pred).ToArray(),
				[AutoPropertyMode.Asset] = (property, pred) => Resources
					.FindObjectsOfTypeAll(property.Field.FieldType.GetElementType() ?? property.Field.FieldType)
					.Where(AssetDatabase.Contains)
					.Where(pred).ToArray(),
				[AutoPropertyMode.Any] = (property, pred) => Resources
					.FindObjectsOfTypeAll(property.Field.FieldType.GetElementType() ?? property.Field.FieldType)
					.Where(pred).ToArray()
			};

		private void FillProperty(SerializedProperty property)
		{
			var apAttribute = (AutoPropertyAttribute)attribute;
			if (apAttribute == null) return;
			Func<Object, bool> predicateMethod = apAttribute.PredicateMethodTarget == null ?
				apAttribute.PredicateMethodName == null ?
				(Func<Object, bool>)(_ => true) :
				(Func<Object, bool>)Delegate.CreateDelegate(typeof(Func<Object, bool>),
					property.serializedObject,
					apAttribute.PredicateMethodName) :
				(Func<Object, bool>)Delegate.CreateDelegate(typeof(Func<Object, bool>),
					apAttribute.PredicateMethodTarget,
					apAttribute.PredicateMethodName);

			var matchedObjects = ObjectsGetters[apAttribute.Mode]
				.Invoke(property, property.serializedObject, predicateMethod);

			if (property.isArray)
			{
				if (matchedObjects != null && (matchedObjects.Length > 0 || apAttribute.AllowEmpty))
				{
					var serializedObject = property.serializedObject;
					var serializedProperty = serializedObject.FindProperty(property.name);
					serializedProperty.ReplaceArray(matchedObjects);
					serializedObject.ApplyModifiedProperties();
					return;
				}
			}
			else
			{
				var obj = matchedObjects.FirstOrDefault();
				if (obj != null || apAttribute.AllowEmpty)
				{
					var serializedObject = property.serializedObject;
					property.objectReferenceValue = obj;
					serializedObject.ApplyModifiedProperties();
					return;
				}
			}

			Debug.LogError($"{property.name} caused: {property.name} is failed to Auto Assign property. No match",
				property.serializedObject.targetObject);
		}
	}
}
#endif
