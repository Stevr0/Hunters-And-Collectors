namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Centralized actor-to-actor hostility rules.
    ///
    /// Keep all social disposition logic here so combat systems never need
    /// special-case checks like "enemy vs player".
    /// </summary>
    public static class HostilityResolver
    {
        public static Disposition GetDisposition(ActorIdentityNet attacker, ActorIdentityNet target)
        {
            // Missing identity means we cannot establish intent safely.
            if (attacker == null || target == null)
                return Disposition.Neutral;

            // Never hostile to self.
            if (ReferenceEquals(attacker, target))
                return Disposition.Friendly;

            int attackerFaction = attacker.GetFactionId();
            int targetFaction = target.GetFactionId();

            // Different factions are hostile by default.
            if (attackerFaction != targetFaction)
                return Disposition.Hostile;

            // Same faction: only player-vs-player with both PvP toggles enabled can be hostile.
            if (IsPlayerActor(attacker) && IsPlayerActor(target) && attacker.GetPvpEnabled() && target.GetPvpEnabled())
                return Disposition.Hostile;

            return Disposition.Friendly;
        }

        public static bool CanAttack(ActorIdentityNet attacker, ActorIdentityNet target)
        {
            return GetDisposition(attacker, target) == Disposition.Hostile;
        }

        private static bool IsPlayerActor(ActorIdentityNet actor)
        {
            return actor != null && actor.NetworkObject != null && actor.NetworkObject.IsPlayerObject;
        }
    }
}
