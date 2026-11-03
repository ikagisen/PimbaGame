using UnityEngine;

namespace PimbaStage
{
    /// <summary>Shows this obstacle (or bridge) only on the chosen Player 1 difficulties.</summary>
    public class DifficultyFilter : MonoBehaviour
    {
        public bool onEasy = true;
        public bool onNormal = true;
        public bool onHard = true;

        public void Apply(Difficulty difficulty)
        {
            bool visible = difficulty == Difficulty.Easy ? onEasy : difficulty == Difficulty.Hard ? onHard : onNormal;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }
    }
}
