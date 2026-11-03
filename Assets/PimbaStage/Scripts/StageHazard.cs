using UnityEngine;

namespace PimbaStage
{
    /// <summary>Costs the performer a heart on touch (stage fire, swinging sandbags). Needs a trigger collider.</summary>
    public class StageHazard : MonoBehaviour
    {
        [Tooltip("Optional parts that flicker like flames.")]
        public Transform[] flickerParts = new Transform[0];

        private Vector3[] baseScales;

        private void Start()
        {
            baseScales = new Vector3[flickerParts.Length];
            for (int i = 0; i < flickerParts.Length; i++)
                if (flickerParts[i] != null) baseScales[i] = flickerParts[i].localScale;
        }

        private void Update()
        {
            for (int i = 0; i < flickerParts.Length; i++)
            {
                if (flickerParts[i] == null) continue;
                float n = Mathf.PerlinNoise(Time.time * 7f, i * 3.7f);
                Vector3 s = baseScales[i];
                flickerParts[i].localScale = new Vector3(s.x * (0.9f + 0.2f * n), s.y * (0.8f + 0.4f * n), s.z);
            }
        }

        private void OnTriggerEnter2D(Collider2D other) => Touch(other);
        private void OnTriggerStay2D(Collider2D other) => Touch(other);

        private void Touch(Collider2D other)
        {
            var player = other.GetComponentInParent<StagePlayer>();
            if (player == null || player.Invulnerable) return;
            var game = StageGame.Instance;
            if (game != null) game.HurtPlayer(false, transform.position.x);
        }
    }
}
