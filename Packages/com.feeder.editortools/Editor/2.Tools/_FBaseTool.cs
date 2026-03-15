using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Feeder
{
    public abstract class FBaseTool : SerializedScriptableObject
    {
        private const string DefaultDescription = "Tool này chưa code, ở đây cho đẹp thôi.";

        [OnInspectorGUI, PropertyOrder(-1000)]
        private void DrawAutoDescription()
        {
            DrawDescription(GetDescription() ?? DefaultDescription);
        }

        protected virtual string GetDescription() => null;

        protected void DrawDescription(string description)
        {
            GUILayout.Space(4);
            StylesUtils.DrawDescription(description);
            GUILayout.Space(6);
        }
    }

    public abstract class FTargetObjectsToolBase : FBaseTool
    {
        [PropertyOrder(-900)]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true, NumberOfItemsPerPage = 10)]
        [OnValueChanged(nameof(HandleTargetObjectsChanged))]
        [ShowInInspector]
        public List<GameObject> TargetObjects
        {
            get => GetTargetObjectsData().TargetObjects;
            set
            {
                var data = GetTargetObjectsData();
                data.TargetObjects.Clear();
                if (value != null)
                    data.TargetObjects.AddRange(value);
                FDataPersistenceService.SaveData(data);
            }
        }

        /// <summary>Persisted data asset so refs survive tool close and Unity restart.</summary>
        protected FDataContainer GetTargetObjectsData() => FDataPersistenceService.GetOrCreateDataContainer();

        protected virtual void OnTargetObjectsChanged()
        {
        }

        private void HandleTargetObjectsChanged()
        {
            FDataPersistenceService.SaveData(GetTargetObjectsData());
            OnTargetObjectsChanged();
        }
    }

    /// <summary>Base for tools that operate on any Unity assets (sprites, scenes, audio, prefabs, etc.).</summary>
    public abstract class FTargetAssetsToolBase : FBaseTool
    {
        private bool _pendingTargetAssetsChange;
        private bool _delayCallScheduled;

        [PropertyOrder(-900)]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true, NumberOfItemsPerPage = 10)]
        [OnValueChanged(nameof(HandleTargetAssetsChanged))]
        [ShowInInspector]
        public List<Object> TargetAssets
        {
            get => GetDataContainer().TargetAssets;
            set
            {
                var c = GetDataContainer();
                c.TargetAssets.Clear();
                if (value != null)
                    c.TargetAssets.AddRange(value);
                FDataPersistenceService.SaveData(c);
            }
        }

        protected FDataContainer GetDataContainer() => FDataPersistenceService.GetOrCreateDataContainer();

        protected virtual void OnTargetAssetsChanged()
        {
        }

        private void HandleTargetAssetsChanged()
        {
            FDataPersistenceService.SaveData(GetDataContainer());
            _pendingTargetAssetsChange = true;
            if (_delayCallScheduled) return;
            _delayCallScheduled = true;
            EditorApplication.delayCall += InvokeDelayedOnTargetAssetsChanged;
        }

        private void InvokeDelayedOnTargetAssetsChanged()
        {
            _delayCallScheduled = false;
            if (!_pendingTargetAssetsChange) return;
            _pendingTargetAssetsChange = false;
            OnTargetAssetsChanged();
        }
    }

    /// <summary>Base for tools that target a single ScriptableObject (e.g. Scriptable Objects Filler).</summary>
    public abstract class FTargetScriptableObjectToolBase : FBaseTool
    {
        [PropertyOrder(-900)]
        [ShowInInspector, OnValueChanged(nameof(HandleTargetSOChanged))]
        public ScriptableObject TargetSO
        {
            get => GetDataContainer().TargetSO;
            set
            {
                GetDataContainer().TargetSO = value;
                FDataPersistenceService.SaveData(GetDataContainer());
            }
        }

        protected FDataContainer GetDataContainer() => FDataPersistenceService.GetOrCreateDataContainer();

        protected virtual void OnTargetSOChanged()
        {
        }

        private void HandleTargetSOChanged()
        {
            FDataPersistenceService.SaveData(GetDataContainer());
            OnTargetSOChanged();
        }
    }

    /// <summary>Base for tools that operate on a list of MeshRenderers (e.g. Deduplicate Mesh).</summary>
    public abstract class FTargetMeshRenderersToolBase : FBaseTool
    {
        [PropertyOrder(-900)]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true, NumberOfItemsPerPage = 10)]
        [OnValueChanged(nameof(HandleTargetsMeshChanged))]
        [ShowInInspector]
        public List<MeshRenderer> TargetsMesh
        {
            get => GetDataContainer().TargetsMesh;
            set
            {
                var c = GetDataContainer();
                c.TargetsMesh.Clear();
                if (value != null)
                    c.TargetsMesh.AddRange(value);
                FDataPersistenceService.SaveData(c);
            }
        }

        protected FDataContainer GetDataContainer() => FDataPersistenceService.GetOrCreateDataContainer();

        protected virtual void OnTargetsMeshChanged()
        {
        }

        private void HandleTargetsMeshChanged()
        {
            FDataPersistenceService.SaveData(GetDataContainer());
            OnTargetsMeshChanged();
        }
    }

    /// <summary>Base for tools that operate on a list of Mesh assets (e.g. Deduplicate Mesh).</summary>
    public abstract class FTargetMeshesToolBase : FBaseTool
    {
        [PropertyOrder(-900)]
        [ListDrawerSettings(ShowFoldout = true, DraggableItems = true, ShowIndexLabels = true, NumberOfItemsPerPage = 10)]
        [OnValueChanged(nameof(HandleTargetMeshesChanged))]
        [ShowInInspector]
        public List<Mesh> TargetMeshes
        {
            get => GetDataContainer().TargetMeshes;
            set
            {
                var c = GetDataContainer();
                c.TargetMeshes.Clear();
                if (value != null)
                    c.TargetMeshes.AddRange(value);
                FDataPersistenceService.SaveData(c);
            }
        }

        protected FDataContainer GetDataContainer() => FDataPersistenceService.GetOrCreateDataContainer();

        protected virtual void OnTargetMeshesChanged()
        {
        }

        private void HandleTargetMeshesChanged()
        {
            FDataPersistenceService.SaveData(GetDataContainer());
            OnTargetMeshesChanged();
        }
    }
}
