using UnityEngine;

namespace PimbaStage
{
    /// <summary>
    /// Player 2's singer, Friday Night Funkin' style: dances on the beat, strikes a pose for every arrow
    /// that is hit (left / down / up / right), slumps on a miss and plays a miss sound.
    /// Sits next to the SpriteFrameAnimator that shows his frames.
    /// </summary>
    [RequireComponent(typeof(SpriteFrameAnimator))]
    public class SingerPoses : MonoBehaviour
    {
        [Tooltip("The arrow lane he reacts to (found automatically if empty).")]
        public RhythmLane lane;

        [Header("Animations (Characters/SingerIdle_*, Left_*, Down_*, Up_*, Right_*, Miss_*)")]
        public Sprite[] idleFrames = new Sprite[0];
        public Sprite[] leftFrames = new Sprite[0];
        public Sprite[] downFrames = new Sprite[0];
        public Sprite[] upFrames = new Sprite[0];
        public Sprite[] rightFrames = new Sprite[0];
        public Sprite[] missFrames = new Sprite[0];
        [Tooltip("Idle frames per beat while a song plays (4 = the whole dance once per beat).")]
        public int idleFramesPerBeat = 4;
        [Tooltip("Idle speed when no song is playing (the GIF uses 140 ms per frame).")]
        public float idleFps = 7.1f;
        [Tooltip("Speed of the arrow and miss poses (the GIF uses 140 ms per frame).")]
        public float poseFps = 7.1f;
        [Tooltip("Seconds he holds an arrow pose before going back to the dance.")]
        public float poseTime = 0.4f;
        [Tooltip("Seconds he holds the miss pose.")]
        public float missTime = 0.55f;

        [Header("Miss sound (Audio/Miss_*.wav, one picked at random)")]
        public AudioClip[] missSounds = new AudioClip[0];
        [Range(0f, 1f)] public float missVolume = 0.7f;

        private SpriteFrameAnimator animator;
        private AudioSource audioSource;
        private float poseUntil;
        private bool posing;
        private int lastSound = -1;

        private void Awake()
        {
            animator = GetComponent<SpriteFrameAnimator>();
            if (lane == null) lane = FindAnyObjectByType<RhythmLane>();
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            BackToIdle();
        }

        private void OnEnable()
        {
            if (lane != null) lane.NoteJudged += OnNoteJudged;
        }

        private void OnDisable()
        {
            if (lane != null) lane.NoteJudged -= OnNoteJudged;
        }

        private void Update()
        {
            if (posing && Time.time >= poseUntil) BackToIdle();
        }

        private void OnNoteJudged(ArrowDir dir, Judgement judgement)
        {
            if (judgement == Judgement.Miss)
            {
                Pose(missFrames, missTime);
                PlayMissSound();
            }
            else Pose(FramesFor(dir), poseTime);
        }

        private Sprite[] FramesFor(ArrowDir dir)
        {
            switch (dir)
            {
                case ArrowDir.Left: return leftFrames;
                case ArrowDir.Down: return downFrames;
                case ArrowDir.Up: return upFrames;
                default: return rightFrames;
            }
        }

        /// <summary>Plays a pose once (holding its last frame), restarting it if it is hit again.</summary>
        private void Pose(Sprite[] clip, float seconds)
        {
            if (clip == null || clip.Length == 0) return;
            posing = true;
            poseUntil = Time.time + seconds;
            animator.framesPerBeat = 0;
            animator.SpeedMultiplier = 1f;
            animator.Play(clip, poseFps, true, false);
        }

        private void BackToIdle()
        {
            posing = false;
            if (idleFrames == null || idleFrames.Length == 0) return;
            animator.framesPerBeat = idleFramesPerBeat;
            animator.Play(idleFrames, idleFps, false, true);
        }

        private void PlayMissSound()
        {
            if (missSounds == null || missSounds.Length == 0 || audioSource == null) return;
            int i = Random.Range(0, missSounds.Length);
            if (missSounds.Length > 1 && i == lastSound) i = (i + 1) % missSounds.Length;
            lastSound = i;
            if (missSounds[i] == null) return;
            audioSource.pitch = Random.Range(0.95f, 1.05f);
            audioSource.PlayOneShot(missSounds[i], missVolume);
        }
    }
}
