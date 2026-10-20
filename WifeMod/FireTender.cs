using System.Collections;
using UnityEngine;

namespace WifeMod
{
    public sealed class FireTender : MonoBehaviour
    {
        public float ScanRadius = 10f;
        public float LowFuelThreshold = 0.3f;
        private WifeCompanion _wife;
        private Humanoid _humanoid;
        private ZNetView _view;
        private Coroutine _routine;
        private float _timer;

        private void Awake() { _wife = GetComponent<WifeCompanion>(); _humanoid = GetComponent<Humanoid>(); _view = GetComponent<ZNetView>(); }
        private void Update()
        {
            if (_wife == null || _humanoid == null || _view == null || !_view.IsOwner() || _routine != null) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 20f;
            Fireplace fire = FindFire();
            if (fire != null) _routine = StartCoroutine(Tend(fire));
        }

        private Fireplace FindFire()
        {
            Fireplace best = null; float lowest = LowFuelThreshold;
            foreach (Collider hit in Physics.OverlapSphere(transform.position, ScanRadius))
            {
                Fireplace fire = hit.GetComponentInParent<Fireplace>();
                ZNetView fireView = fire != null ? fire.GetComponent<ZNetView>() : null;
                if (fire == null || fireView == null || fireView.GetZDO() == null || fire.m_fuelItem == null) continue;
                string fuelName = fire.m_fuelItem.m_itemData.m_shared.m_name;
                if (!_humanoid.GetInventory().HaveItem(fuelName, true)) continue;
                float pct = fireView.GetZDO().GetFloat("fuel", 0f) / Mathf.Max(1f, fire.m_maxFuel);
                if (pct < lowest) { lowest = pct; best = fire; }
            }
            return best;
        }

        private IEnumerator Tend(Fireplace fire)
        {
            WifeState previous = _wife.State; _wife.State = WifeState.TendingFire;
            float timeout = 20f;
            while (fire != null && Vector3.Distance(transform.position, fire.transform.position) > 1.8f && timeout > 0f)
            { timeout -= Time.deltaTime; _wife.MoveTo(fire.transform.position, 1.5f); yield return null; }
            if (fire != null && timeout > 0f)
            {
                ZNetView fireView = fire.GetComponent<ZNetView>();
                string fuelName = fire.m_fuelItem.m_itemData.m_shared.m_name;
                while (fireView != null && fireView.GetZDO() != null && fireView.GetZDO().GetFloat("fuel", 0f) < fire.m_maxFuel)
                {
                    ItemDrop.ItemData wood = _humanoid.GetInventory().GetItem(fuelName, -1, true);
                    if (wood == null || !_humanoid.GetInventory().RemoveItem(wood, 1)) break;
                    fire.AddFuel(1f);
                    yield return new WaitForSeconds(0.8f);
                }
            }
            _wife.State = previous; _routine = null;
        }
    }
}
