using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PimbaStage
{
    /// <summary>Menu button feel: grows when hovered or selected with the keyboard/gamepad, squashes when pressed.</summary>
    public class MenuButtonFX : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IDeselectHandler, IPointerDownHandler, ISubmitHandler
    {
        public float selectedScale = 1.08f;

        private Vector3 baseScale = Vector3.one;
        private bool selected;
        private float press;

        private void Awake()
        {
            baseScale = transform.localScale;
        }

        private void OnEnable()
        {
            transform.localScale = baseScale;
            selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            var selectable = GetComponent<Selectable>();
            if (EventSystem.current != null && selectable != null && selectable.IsInteractable())
                EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public void OnSelect(BaseEventData eventData) => selected = true;
        public void OnDeselect(BaseEventData eventData) => selected = false;
        public void OnPointerDown(PointerEventData eventData) => press = 1f;
        public void OnSubmit(BaseEventData eventData) => press = 1f;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            press = Mathf.MoveTowards(press, 0f, dt * 6f);
            float target = (selected ? selectedScale : 1f) - press * 0.08f;
            transform.localScale = Vector3.Lerp(transform.localScale, baseScale * target, 15f * dt);
        }
    }
}
