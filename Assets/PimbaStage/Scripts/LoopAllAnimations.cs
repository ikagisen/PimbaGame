using UnityEngine;

namespace PimbaStage
{
    /// <summary>Plays every animation clip on this object's legacy Animation component, looping, all at once.</summary>
    public class LoopAllAnimations : MonoBehaviour
    {
        private void Start()
        {
            var anim = GetComponent<Animation>();
            if (anim == null) return;
            int layer = 0;
            foreach (AnimationState state in anim)
            {
                state.wrapMode = WrapMode.Loop;
                state.layer = layer++;
                anim.Play(state.name);
            }
        }
    }
}
