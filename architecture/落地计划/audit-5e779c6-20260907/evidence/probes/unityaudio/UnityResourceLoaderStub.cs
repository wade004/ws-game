using Core.Foundation.Common;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter;

public sealed class UnityResourceLoader
{
    public bool TryGetAudioClip(Id id, out AudioClip clip)
    {
        clip = new AudioClip(id.Value);
        return true;
    }
}
