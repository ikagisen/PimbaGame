using System;
using UnityEngine;

namespace PimbaStage
{
    /// <summary>
    /// Plays the song and keeps an accurate song clock (synced to the audio hardware clock),
    /// so arrows line up with the music. Raises OnBeat for lights, camera bops and dancing.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class Conductor : MonoBehaviour
    {
        public PimbaSong Song { get; private set; }
        /// <summary>Seconds into the audio. Negative during the count-in.</summary>
        public double SongTime { get; private set; }
        public double Beat => Song != null ? Song.TimeToBeat(SongTime) : 0.0;
        public double Length { get; private set; }
        public bool IsRunning { get; private set; }
        public bool Finished { get; private set; }

        public event Action<int> OnBeat;

        private AudioSource source;
        private double startDsp, pauseDsp;
        private bool paused;
        private int lastBeat;
        private float baseVolume = 1f, fadeSpeed;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
        }

        public void Load(PimbaSong song, AudioClip clip)
        {
            Song = song;
            source.clip = clip;
            baseVolume = song != null ? song.volume : 1f;
            source.volume = baseVolume;
            Length = clip != null ? clip.length : 0.0;
            Finished = false;
            IsRunning = false;
        }

        /// <summary>Starts the song after a count-in (in seconds).</summary>
        public void Play(double countIn)
        {
            const double scheduleDelay = 0.1;
            startDsp = AudioSettings.dspTime + countIn + scheduleDelay;
            source.volume = baseVolume;
            source.PlayScheduled(startDsp);
            SongTime = -(countIn + scheduleDelay);
            lastBeat = (int)Math.Floor(Beat);
            IsRunning = true;
            Finished = false;
            paused = false;
            fadeSpeed = 0f;
        }

        public void SetPaused(bool value)
        {
            if (!IsRunning || value == paused) return;
            paused = value;
            if (paused)
            {
                pauseDsp = AudioSettings.dspTime;
                source.Pause();
            }
            else
            {
                startDsp += AudioSettings.dspTime - pauseDsp;
                if (SongTime < 0)
                {
                    source.Stop();
                    source.PlayScheduled(startDsp);
                }
                else
                {
                    source.UnPause();
                }
            }
        }

        public void FadeOut(float seconds)
        {
            fadeSpeed = Mathf.Max(0.01f, source.volume) / Mathf.Max(0.01f, seconds);
        }

        private void Update()
        {
            if (fadeSpeed > 0f && source.isPlaying)
            {
                source.volume = Mathf.MoveTowards(source.volume, 0f, fadeSpeed * Time.unscaledDeltaTime);
                if (source.volume <= 0f) source.Stop();
            }

            if (!IsRunning || paused) return;

            // Smooth clock: advance with frame time, gently corrected towards the audio clock.
            double dspSongTime = AudioSettings.dspTime - startDsp;
            SongTime += Time.unscaledDeltaTime;
            double drift = dspSongTime - SongTime;
            if (Math.Abs(drift) > 0.05) SongTime = dspSongTime;
            else SongTime += drift * 0.1;

            int beat = (int)Math.Floor(Beat);
            while (lastBeat < beat)
            {
                lastBeat++;
                OnBeat?.Invoke(lastBeat);
            }

            if (SongTime >= Length + 0.1)
            {
                Finished = true;
                IsRunning = false;
            }
        }
    }
}
