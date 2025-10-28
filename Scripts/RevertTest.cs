/*using UnityEngine;
using System.Collections.Generic;

public class MutationTest : MonoBehaviour
{
    public GameObject testPrefab;

    void Start()
    {
        // Create dummy SceneState
        var state = ScriptableObject.CreateInstance<SceneState>();
        state.objects = new List<GameObject>();
        state.lights = new List<PlacedLight>();

        Debug.Log($"Initial object count: {state.objects.Count}");

        // Create mutation helper
        var mutation = new SceneSynthesizer.Mutation(state, new List<GameObject> { testPrefab }, maxObjs: 5, minObjs: 0);

        // Force "Add object" mutation
        mutation.ForceAddObject();

        Debug.Log($"After Add: object count = {state.objects.Count}");
        if (state.objects.Count > 0) Debug.Log($"Added object: {state.objects[0].name}");

        // Revert the mutation
        mutation.Revert();

        Debug.Log($"After Revert: object count = {state.objects.Count}");
    }
}

public static class MutationExtensions
{
    public static void ForceAddObject(this SceneSynthesizer.Mutation m)
    {
        // Mimic the add-object logic
        var addedObjectField = typeof(SceneSynthesizer.Mutation).GetField("addedObject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var stateField = typeof(SceneSynthesizer.Mutation).GetField("state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var objectPrefabsField = typeof(SceneSynthesizer.Mutation).GetField("objectPrefabs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var state = (SceneState)stateField.GetValue(m);
        var prefabList = (List<GameObject>)objectPrefabsField.GetValue(m);

        var prefab = prefabList[0];
        var obj = Object.Instantiate(prefab);
        obj.transform.position = Vector3.zero;
        obj.transform.rotation = Quaternion.identity;

        state.objects.Add(obj);

        addedObjectField.SetValue(m, obj);
    }
}
*/