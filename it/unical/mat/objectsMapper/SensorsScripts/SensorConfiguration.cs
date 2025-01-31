﻿using System;
using System.Collections.Generic;
using System.Reflection;
using ThinkEngine.Mappers;
#if UNITY_EDITOR
    using UnityEditor;
#endif
using ThinkEngine.ScriptGeneration;
using UnityEngine;
using System.IO;
using UnityEditor.SceneManagement;

namespace ThinkEngine
{
    [ExecuteAlways, Serializable]
    public class SensorConfiguration : AbstractConfiguration//, ISerializationCallbackReceiver
    {
        public bool isInvariant;
        public bool isFixedSize;

        //GMDG
        //This array contains the types of the sensor assiated with "this" SensorConfiguration

        [SerializeField]
        internal List<SerializableSensorType> _serializableSensorsTypes = new List<SerializableSensorType>();
        [SerializeField,HideInInspector]
        internal bool recompile;
        [SerializeField, HideInInspector]
        internal bool teRecompile;
        private bool forceRecompile;
        [SerializeField]
        internal List<string> generatedScripts= new List<string>();
        private List<Sensor> _sensorsInstances = new List<Sensor>();

        internal static SensorConfiguration _instance;
        internal static SensorConfiguration Instance
        {
            get
            {
                if (_instance == null || !_instance.enabled)
                {
                    foreach(SensorConfiguration s in FindObjectsOfType<SensorConfiguration>())
                    {
                        if(s != null && s.enabled)
                        {
                            _instance = s;
                        }
                    }
                }
                return _instance;
            }
        }

        void Awake()
        {
            if (Application.isPlaying)
            {
   
                foreach (SerializableSensorType serializableSensorType in _serializableSensorsTypes)
                {
                    //                   _sensorsInstances.Add((Sensor)serializableSensorType.ScriptType.GetProperty("Instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
                    
                        _sensorsInstances.Add((Sensor)Activator.CreateInstance(serializableSensorType.ScriptType));
                    /*using (StreamWriter fs = new StreamWriter(Path.Combine(Path.GetTempPath(), "ThinkEngineFacts", "Log.log"), true))
                    {
                        fs.Write(_sensorsInstances[_sensorsInstances.Count - 1] + Environment.NewLine);
                    }*/
                    Debug.Log(_sensorsInstances[_sensorsInstances.Count - 1]);

                }
                foreach (Sensor instance in _sensorsInstances)
                {
                    instance.Initialize(this);
                }
            }
        }

        internal override void PropertyAliasChanged(string oldAlias, string newAlias)
        {
#if UNITY_EDITOR
            if (PrefabStageUtility.GetPrefabStage(gameObject) != null)
            {
                foreach (SensorConfiguration s in Resources.FindObjectsOfTypeAll<SensorConfiguration>())
                {
                    if(s!=null && s!=this && s.ConfigurationName == this.ConfigurationName)
                    {
                        foreach(PropertyFeatures pF in s.PropertyFeaturesList)
                        {
                            if (pF.PropertyAlias == oldAlias)
                            {
                                Debug.Log("Changing name to " + s.gameObject);
                                pF.AssignPropertyAliasWithoutValidation( newAlias);
                                CodeGenerator.Rename(oldAlias, newAlias, s);
                                break;
                            }
                        }
                    }
                }
            }
            CodeGenerator.Rename(oldAlias,newAlias,this);
            recompile = true;
#endif
        }
#if UNITY_EDITOR
        [UnityEditor.Callbacks.DidReloadScripts]
#endif
        static void Reload()
        {
            Debug.Log("Did Reload");
            if (Instance == null)
            {
                Debug.Log("Instance is null.");
            }
            else if (Instance.teRecompile)
            {
                Debug.Log("TE recompiled.");
                Instance.teRecompile = false;
            }
            else
            {
                Debug.Log("Forcing TE recompile.");
                Instance.teRecompile=true;
                Instance.forceRecompile=true;
                return;
            }

            foreach (SensorConfiguration sensorConfiguration in Resources.FindObjectsOfTypeAll<SensorConfiguration>())
            {
                if (sensorConfiguration != null)
                {
                    CodeGenerator.AttachSensorsScripts(sensorConfiguration);
                }
            }
        }
        static void Recompile()
        {
#if UNITY_EDITOR
            if (Instance != null)
            {
                Instance.teRecompile = true;
            }
            Utility.LoadPrefabs();
            foreach (SensorConfiguration sensorConfiguration in Resources.FindObjectsOfTypeAll<SensorConfiguration>())
            {
                sensorConfiguration.GenerateScripts(false);
            }
            AssetDatabase.Refresh();
#endif
        }

        internal void GenerateScripts(bool _recompile=true)
        {
#if UNITY_EDITOR
            CodeGenerator.GenerateCode(this);
            recompile = _recompile;
            //CompilationPipeline.RequestScriptCompilation();
#endif
        }
        void Start()
        {
            /*
            using (StreamWriter fs = new StreamWriter(Path.Combine(Path.GetTempPath(), "ThinkEngineFacts", "Log.log"), true))
            {
                fs.Write(ConfigurationName+" configuration started" + Environment.NewLine);
            }
            */
            Utility.LoadPrefabs();

        }
        void OnEnable()
        {
            if (Application.isPlaying)
            {
                SensorsManager.SubscribeSensors(_sensorsInstances, ConfigurationName);
                foreach (Sensor s in _sensorsInstances)
                {
                    s.Enable();
                }
            }
        }

        void OnDisable()
        {
            if (Application.isPlaying)
            {
                foreach(Sensor s in _sensorsInstances)
                {
                    s.Disable();
                }
                SensorsManager.UnsubscribeSensors(_sensorsInstances, ConfigurationName);
            }
        }

        void OnDestroy()
        {
            if (Application.isPlaying)
            {
                foreach(Sensor instance in _sensorsInstances)
                {
                    instance.Destroy();
                }
            }
#if UNITY_EDITOR
            if (recompile)
            {
                Recompiling();
        }
#endif
        }
        //GMDG

        internal override string ConfigurationName
        {
            set
            {
                if (!Utility.SensorsManager.IsConfigurationNameValid(value, this))
                {
                    throw new Exception("The chosen configuration name cannot be used.");
                }
                string old = _configurationName;
                _configurationName = value;
                if (!old.Equals(_configurationName))
                {
                    SensorsManager.ConfigurationsChanged = true;
                }
            }
        }

        internal override void Clear()
        {
            base.Clear();
            _serializableSensorsTypes = new List<SerializableSensorType>(); // GMDG
        }
        internal override string GetAutoConfigurationName()
        {
            string name;
            string toAppend = "";
            int count = 0;
            do
            {
                name = ASPMapperHelper.AspFormat(gameObject.name) + "Sensor" + toAppend;
                toAppend += count;
                count++;
            }
            while (!Utility.SensorsManager.IsConfigurationNameValid(name, this));
            return name;
        }
        internal void SetOperationPerProperty(MyListString property, int operation)
        {
            if (!SavedProperties.Contains(property))
            {
                throw new Exception("Property not selected");
            }
            PropertyFeaturesList.Find(x => x.property.Equals(property)).operation = operation;
        }
        internal void SetSpecificValuePerProperty(MyListString property, string value)
        {
            if (!SavedProperties.Contains(property))
            {
                throw new Exception("Property not selected");
            }
            PropertyFeaturesList.Find(x => x.property.Equals(property)).specificValue = value;

        }

        internal void SetCounterPerProperty(MyListString actualProperty, int newCounter)
        {
            if (!SavedProperties.Contains(actualProperty))
            {
                throw new Exception("Property not selected");
            }
            PropertyFeaturesList.Find(x => x.property.Equals(actualProperty)).counter = newCounter;
        }

        internal override bool IsSensor()
        {
            return true;
        }
        
        protected override void PropertySelected(MyListString property)
        {
#if UNITY_EDITOR
            GenerateScripts();
#endif
        }
        protected override void PropertyDeleted(MyListString property) 
        {
#if UNITY_EDITOR
            if (ToMapProperties.Contains(property))
            {
                CodeGenerator.RemoveUseless(property, this);
            }
#endif
        }
#if UNITY_EDITOR
        void Update()
        {
            if (InEditMode()) 
            {
                if (EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.titleContent.text != "Inspector" && recompile)
                {
                    Recompiling();
                }
                else
                {
                    
                }
            }

        }

        private void Recompiling()
        {
            Debug.LogWarning("Compiling " + ConfigurationName + " generated scripts.");
            recompile = false;
            //CompilationPipeline.RequestScriptCompilation();
            if (Instance != null)
            {
                Instance.forceRecompile = true;
            }
            else
            {
                forceRecompile = true;
            }
            AssetDatabase.Refresh();
        }

        void LateUpdate()
        {
            if (forceRecompile)
            {
                forceRecompile = false;
                Recompile();
            }
        }
        private bool InEditMode()
        {
            return !(EditorApplication.isPlaying || EditorApplication.isCompiling
                || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.isUpdating);

        }
#endif
        /*
protected override void PropertyDeleted(MyListString property)
{

}

public void OnBeforeSerialize()
{
operationPerPropertyIndexes = new List<int>();
operationPerPropertyOperations = new List<int>();
specificValuePerPropertyIndexes = new List<int>();
specificValuePerPropertyValues = new List<string>();
foreach (int key in OperationPerProperty.Keys)
{
operationPerPropertyIndexes.Add(key);
operationPerPropertyOperations.Add(OperationPerProperty[key]);
}
foreach (int key in SpecificValuePerProperty.Keys)
{
specificValuePerPropertyIndexes.Add(key);
specificValuePerPropertyValues.Add(SpecificValuePerProperty[key]);
}
}

public void OnAfterDeserialize()
{
OperationPerProperty = new Dictionary<int, int>();
SpecificValuePerProperty = new Dictionary<int, string>();
for (int i = 0; i < operationPerPropertyIndexes.Count; i++)
{
OperationPerProperty.Add(operationPerPropertyIndexes[i], operationPerPropertyOperations[i]);
}
for (int i = 0; i < specificValuePerPropertyIndexes.Count; i++)
{
SpecificValuePerProperty.Add(specificValuePerPropertyIndexes[i], specificValuePerPropertyValues[i]);
}
}
*/
        internal override bool IsAValidName(string temporaryName)
        {
            return temporaryName.Equals(ConfigurationName) || Utility.SensorsManager.IsConfigurationNameValid(temporaryName, this);
        }

    }
}