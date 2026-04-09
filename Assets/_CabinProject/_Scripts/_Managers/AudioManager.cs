using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace CabinProject
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        public void PlayOneShot(EventReference sound, Vector3 worldPos)
        {
            RuntimeManager.PlayOneShot(sound, worldPos);
        }

        public void PlayOneShotWithParameters(
            EventReference sound,
            Vector3 worldPos,
            IReadOnlyList<(string Name, float Value)> parameters,
            float volume = 1f)
        {
            if (sound.IsNull)
            {
                return;
            }

            EventInstance instance = RuntimeManager.CreateInstance(sound);
            instance.set3DAttributes(RuntimeUtils.To3DAttributes(worldPos));
            instance.setVolume(Mathf.Max(0f, volume));

            if (parameters != null)
            {
                for (int i = 0; i < parameters.Count; i++)
                {
                    instance.setParameterByName(parameters[i].Name, parameters[i].Value);
                }
            }

            instance.start();
            instance.release();
        }
    }
}
