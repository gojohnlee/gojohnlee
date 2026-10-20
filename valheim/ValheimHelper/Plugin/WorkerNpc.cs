using System.Collections.Generic;
using UnityEngine;
using ValheimHelper.Core.Npc;

namespace ValheimHelper
{
    /// <summary>
    /// 일꾼 NPC 몸 (구상서 3장). Player 프리팹을 복제해 Player 부품 대신 붙는다(WorkerPrefab).
    /// 이름·성향·거점은 ZDO에 저장해 월드와 함께 남는다. 계산은 호스트가 한다(FarZones로 소유 유지).
    /// </summary>
    public class WorkerNpc : Humanoid
    {
        public static readonly List<WorkerNpc> All = new List<WorkerNpc>();

        public const string KeyName = "vh_name";
        public const string KeyRole = "vh_role";
        public const string KeyHome = "vh_home";
        /// <summary>처음 나타날 때 인사할지(새로 만든 NPC).</summary>
        public const string KeyGreet = "vh_greet";

        internal WorkerAI Ai;

        public const string KeyInventory = "vh_inv";

        protected override void Awake()
        {
            base.Awake();
            Ai = GetComponent<WorkerAI>();
            All.Add(this);
            if (m_nview.IsValid()) Workers.Know(m_nview.GetZDO().m_uid);
            // 바닐라 몬스터는 소지품을 저장하지 않는다. 일꾼은 들고 있던 짐·도구를 ZDO에 저장해 둔다.
            if (m_nview.IsValid() && m_nview.IsOwner())
            {
                byte[] data = m_nview.GetZDO().GetByteArray(KeyInventory);
                if (data != null) m_inventory.Load(new ZPackage(data));
            }
            m_inventory.m_onChanged = (System.Action)System.Delegate.Combine(m_inventory.m_onChanged, new System.Action(SaveInventory));
        }

        void SaveInventory()
        {
            if (m_nview == null || !m_nview.IsValid() || !m_nview.IsOwner()) return;
            var pkg = new ZPackage();
            m_inventory.Save(pkg);
            m_nview.GetZDO().Set(KeyInventory, pkg.GetArray());
        }

        /// <summary>운반 한도: 플레이어 기본값과 같은 300 (구상서 3장; 힘의 벨트는 ③에서).</summary>
        public float MaxCarry => 300f;

        /// <summary>시험 기록용: 이동 입력 크기, 달리기 입력, 실제 속도 값.</summary>
        public string MoveDebug => $"입력 {m_moveDir.magnitude:0.00}, 달리기 입력 {m_run}, 속도값 {m_speed}/{m_runSpeed}";

        /// <summary>그 종류의 도구 중 가장 좋은 것(도구 단계 → 해당 피해 순).</summary>
        public static ItemDrop.ItemData BestTool(Inventory inv, ToolKind kind)
        {
            ItemDrop.ItemData best = null;
            foreach (var it in inv.GetAllItems())
            {
                if (!IsTool(it, kind) || it.m_durability <= 0f && it.m_shared.m_useDurability) continue;
                if (best == null || Score(it, kind) > Score(best, kind)) best = it;
            }
            return best;
        }

        public static bool IsTool(ItemDrop.ItemData it, ToolKind kind) =>
            it.IsWeapon() && (kind == ToolKind.Axe ? it.m_shared.m_skillType == Skills.SkillType.Axes
                            : kind == ToolKind.Pickaxe && it.m_shared.m_skillType == Skills.SkillType.Pickaxes);

        static float Score(ItemDrop.ItemData it, ToolKind kind)
        {
            var d = it.GetDamage();
            return it.m_shared.m_toolTier * 1000f + (kind == ToolKind.Axe ? d.m_chop : d.m_pickaxe);
        }

        /// <summary>도구를 손에 든다(이미 들고 있으면 그대로).</summary>
        public bool HoldTool(ToolKind kind)
        {
            ItemDrop.ItemData cur = GetCurrentWeapon();
            if (cur != null && IsTool(cur, kind) && !(cur.m_shared.m_useDurability && cur.m_durability <= 0f)) return true;
            ItemDrop.ItemData best = BestTool(m_inventory, kind);
            return best != null && EquipItem(best);
        }

        public int CountItem(string prefab)
        {
            int n = 0;
            foreach (var it in m_inventory.GetAllItems())
                if (it.m_dropPrefab != null && it.m_dropPrefab.name == prefab) n += it.m_stack;
            return n;
        }

        // Humanoid.Start는 몬스터용 기본 장비를 준다. 장비는 상자에서 받으므로(4장) 하지 않는다.
        protected override void Start()
        {
            if (!m_nview.IsValid() || !m_nview.IsOwner()) return;
            ApplyLook();
            if (m_nview.GetZDO().GetBool(KeyGreet))
            {
                m_nview.GetZDO().Set(KeyGreet, false);
                Say("spawn");
            }
        }

        protected override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public string NpcName => m_nview != null && m_nview.IsValid() ? m_nview.GetZDO().GetString(KeyName, "Worker") : "Worker";
        public Personality Personality => m_nview != null && m_nview.IsValid() ? (Personality)m_nview.GetZDO().GetInt(KeyRole) : Personality.Scv;
        public Vector3 Home => m_nview.GetZDO().GetVec3(KeyHome, transform.position);

        public override string GetHoverName() => NpcName;

        // Character.TeleportTo는 비어 있다(플레이어만 순간이동). 막혔을 때 거점 복귀 등에 쓴다.
        public override bool TeleportTo(Vector3 pos, Quaternion rot, bool distantTeleport)
        {
            if (!m_nview.IsValid()) return false;
            if (!m_nview.IsOwner())
            {
                m_nview.InvokeRPC("RPC_TeleportTo", pos, rot, distantTeleport);
                return true;
            }
            if (ZoneSystem.instance.GetSolidHeight(pos, out float h)) pos.y = Mathf.Max(pos.y, h + 0.2f);
            transform.position = pos;
            transform.rotation = rot;
            m_body.linearVelocity = Vector3.zero;
            return true;
        }

        /// <summary>
        /// 외형: 둘 다 남자 바이킹, 1호는 갈색 머리·수염, 2호는 검은 머리·짧은 수염.
        /// 소유자 VisEquipment는 자기 필드를 쓰므로 생성될 때마다 다시 넣는다(바뀐 값만 ZDO에 기록된다).
        /// </summary>
        void ApplyLook()
        {
            VisEquipment vis = GetComponent<VisEquipment>();
            if (vis == null) return;
            bool scv = Personality == Personality.Scv;
            vis.SetModel(0);
            vis.SetHairItem((scv ? LookHair[0] : LookHair[1]).GetStableHashCode());
            vis.SetBeardItem((scv ? LookBeard[0] : LookBeard[1]).GetStableHashCode());
            vis.SetSkinColor(scv ? new Vector3(1f, 0.92f, 0.85f) : new Vector3(0.82f, 0.7f, 0.6f));
            vis.SetHairColor(scv ? new Vector3(0.45f, 0.35f, 0.25f) : new Vector3(0.08f, 0.08f, 0.08f));
        }

        public static readonly string[] LookHair = { "Hair2", "Hair1" };
        public static readonly string[] LookBeard = { "Beard5", "Beard2" };

        // ---- 말하기 ----

        static readonly Speech s_speech = new Speech();

        public void Say(string key, IDictionary<string, string> args = null) => SayText(s_speech.Say(Personality, key, args));

        public string Line(string key, IDictionary<string, string> args = null) => s_speech.Say(Personality, key, args);

        public void SayText(string text) => ChatNet.NpcSay(this, text);
    }
}
