using UnityEngine;

namespace PimbaStage
{
    /// <summary>The finale spotlight: the performer standing here wins the show. Needs a trigger collider.</summary>
    public class StageGoal : MonoBehaviour
    {
        public Transform star;
        public float spinSpeed = 90f;

        private void Update()
        {
            if (star != null) star.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self); // 2.5D spin
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponentInParent<StagePlayer>() == null) return;
            var game = StageGame.Instance;
            if (game != null) game.Win();
        }
    }
}
