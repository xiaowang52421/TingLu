using UnityEngine;
using TingLu.Contracts;
namespace TingLu.Bootstrap
{
    public sealed class BootstrapStatus : MonoBehaviour
    {
        private void Start()
        { Debug.Log("听路 CONTRACTS_READY " + ContractVersion.Current + "; G0 skeleton only. Gameplay modules must be wired before Demo acceptance."); }
        private void OnGUI()
        { GUI.Label(new Rect(20, 20, 900, 60), "TingLu G0 / Contracts " + ContractVersion.Current + " / Gameplay integration pending"); }
    }
}
