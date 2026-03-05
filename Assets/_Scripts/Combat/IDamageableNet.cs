namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// Contract for server-authoritative damage receivers.
    ///
    /// IMPORTANT:
    /// - This method is intended to be called on the server only.
    /// - Implementations must never trust client-side health authority.
    /// </summary>
    public interface IDamageableNet
    {
        bool ServerTryApplyDamage(int amount, ulong attackerClientId, UnityEngine.Vector3 hitPoint);
    }
}
