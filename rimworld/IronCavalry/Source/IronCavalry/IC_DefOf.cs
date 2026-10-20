using RimWorld;
using Verse;

namespace IronCavalry
{
    [DefOf]
    public static class IC_DefOf
    {
        public static JobDef GJL_IC_Charge;
        public static HediffDef GJL_IC_ChargeSpeed;
        public static KeyBindingDef GJL_IC_ChargeKey;

        static IC_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(IC_DefOf));
        }
    }
}
