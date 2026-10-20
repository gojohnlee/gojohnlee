using UnityEngine;
using Verse;

namespace IronCavalry
{
    [StaticConstructorOnStartup]
    public static class IC_Textures
    {
        public static readonly Texture2D Charge = ContentFinder<Texture2D>.Get("UI/Commands/GJL_IC_Charge");
        public static readonly Texture2D AutoCharge = ContentFinder<Texture2D>.Get("UI/Commands/GJL_IC_AutoCharge");
    }
}
