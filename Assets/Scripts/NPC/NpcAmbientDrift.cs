using Rpg.Dialogue;
using UnityEngine;

namespace Rpg.Npc
{
    /// <summary>Keeps NPC grounded and stationary so idle dialogue reads clearly.</summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(15)]
    public sealed class NpcAmbientDrift : MonoBehaviour
    {
        [SerializeField] float gravity = -18f;

        CharacterController _cc;
        float _verticalVelocity;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
        }

        void Update()
        {
            if (_cc == null)
                _cc = GetComponent<CharacterController>();
            if (_cc == null || !_cc.enabled || !_cc.gameObject.activeInHierarchy)
                return;

            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen)
            {
                if (_cc.isGrounded && _verticalVelocity < 0f)
                    _verticalVelocity = -1f;
                _verticalVelocity += gravity * Time.deltaTime;
                _cc.Move(Vector3.up * (_verticalVelocity * Time.deltaTime));
                return;
            }

            if (_cc.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -1f;
            _verticalVelocity += gravity * Time.deltaTime;
            _cc.Move(Vector3.up * (_verticalVelocity * Time.deltaTime));
        }
    }
}
