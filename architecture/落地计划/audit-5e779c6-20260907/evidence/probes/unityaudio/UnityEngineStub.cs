using System.Collections.Generic;

namespace UnityEngine;

public class Object
{
}

public class Component : Object
{
    public GameObject gameObject { get; internal set; } = null!;
    public Transform transform => gameObject.transform;
}

public sealed class Transform : Component
{
    public Vector3 position;
    public Transform? parent;

    public void SetParent(Transform? value, bool worldPositionStays)
    {
        parent = value;
    }
}

public sealed class GameObject : Object
{
    public static readonly List<GameObject> All = new();
    public string name;
    public Transform transform { get; }

    public GameObject(string name)
    {
        this.name = name;
        transform = new Transform { gameObject = this };
        All.Add(this);
    }

    public T AddComponent<T>() where T : Component, new()
    {
        var component = new T { gameObject = this };
        return component;
    }
}

public sealed class AudioClip : Object
{
    public string Name { get; }
    public AudioClip(string name) => Name = name;
}

public sealed class AudioSource : Component
{
    public AudioClip? clip;
    public bool loop;
    public bool playOnAwake;
    public float volume;
    public float pitch;
    public float spatialBlend;
    public bool isPlaying { get; private set; }

    public void Play() => isPlaying = true;
    public void Stop() => isPlaying = false;
    public void SimulateNaturalEnd() => isPlaying = false;
}

public readonly struct Vector3
{
    public readonly float x;
    public readonly float y;
    public readonly float z;
    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
}

public static class Mathf
{
    public static float Max(float a, float b) => a > b ? a : b;
}

public static class Debug
{
    public static readonly List<string> Warnings = new();
    public static void LogWarning(string message) => Warnings.Add(message);
}
