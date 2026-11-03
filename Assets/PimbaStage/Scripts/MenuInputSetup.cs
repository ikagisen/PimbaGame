using UnityEngine;
using UnityEngine.InputSystem.UI;

namespace PimbaStage
{
    /// <summary>Makes sure the menu's input module has its default keyboard / mouse / gamepad actions.</summary>
    [DefaultExecutionOrder(-1000)]
    public class MenuInputSetup : MonoBehaviour
    {
        private void Awake()
        {
            var module = GetComponent<InputSystemUIInputModule>();
            if (module != null && module.actionsAsset == null) module.AssignDefaultActions();
        }
    }
}
