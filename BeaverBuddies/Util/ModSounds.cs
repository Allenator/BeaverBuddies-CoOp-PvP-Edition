using System.IO;
using System.Linq;
using Timberborn.SingletonSystem;
using Timberborn.SoundSystem;
using UnityEngine;

namespace BeaverBuddies.Util
{
    /**
     * The mod's own sounds: every .wav in its Sounds folder, added to the
     * game's sounds under its file name. A building's blueprint names its
     * selection sound without the "UI.Buildings.Selected." prefix, so
     * UI.Buildings.Selected.BB.Coins.wav plays for "SelectionSoundName":
     * "BB.Coins". The game can't load sounds from a mod by itself.
     */
    public class ModSounds : ILoadableSingleton
    {
        public static string ModPath;
        private readonly AudioClipService _audioClipService;

        public ModSounds(AudioClipService audioClipService)
        {
            _audioClipService = audioClipService;
        }

        public void Load()
        {
            if (ModPath == null || !Directory.Exists(ModPath)) return;
            foreach (string folder in Directory.GetDirectories(ModPath, "Sounds", SearchOption.AllDirectories))
            foreach (string file in Directory.GetFiles(folder, "*.wav"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                AudioClip clip = Wav.Read(file, name);
                if (clip) _audioClipService.AddAudioClip(name, clip);
                else Plugin.LogWarning($"Couldn't read sound {file}: only 16-bit PCM .wav files work");
            }
        }
    }

    // A sound of ours that didn't load plays the game's generic one: the game throws on a sound with no files
    [HarmonyLib.HarmonyPatch(typeof(Timberborn.SoundSystem.Sounds), nameof(Timberborn.SoundSystem.Sounds.GetRandomSound))]
    class ModSoundsFallbackPatcher
    {
        private const string OurSounds = "UI.Buildings.Selected.BB.";

        static void Prefix(Timberborn.SoundSystem.Sounds __instance, ref string soundName)
        {
            if (soundName == null || !soundName.StartsWith(OurSounds)) return;
            if (__instance._audioClipService.GetAudioClipNames(soundName).Any()) return;
            soundName = "UI.Buildings.Selected.Default";
        }
    }

    // Plain 16-bit PCM .wav files, read without Unity's importer
    static class Wav
    {
        public static AudioClip Read(string path, string name)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 44 || System.Text.Encoding.ASCII.GetString(data, 0, 4) != "RIFF") return null;
            int channels = 0, rate = 0, bits = 0, at = 12;
            while (at + 8 <= data.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(data, at, 4);
                int size = System.BitConverter.ToInt32(data, at + 4);
                int body = at + 8;
                if (id == "fmt ")
                {
                    if (System.BitConverter.ToInt16(data, body) != 1) return null;
                    channels = System.BitConverter.ToInt16(data, body + 2);
                    rate = System.BitConverter.ToInt32(data, body + 4);
                    bits = System.BitConverter.ToInt16(data, body + 14);
                }
                else if (id == "data")
                {
                    if (bits != 16 || channels <= 0) return null;
                    size = System.Math.Min(size, data.Length - body);
                    int count = size / 2;
                    var samples = new float[count];
                    for (int i = 0; i < count; i++) samples[i] = System.BitConverter.ToInt16(data, body + i * 2) / 32768f;
                    AudioClip clip = AudioClip.Create(name, count / channels, channels, rate, false);
                    clip.SetData(samples, 0);
                    return clip;
                }
                at = body + size + (size & 1);
            }
            return null;
        }
    }
}
