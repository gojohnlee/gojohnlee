using System.Collections.Generic;

namespace ValheimHelper.Core.Storage
{
    /// <summary>
    /// 사람끼리 동시 열기(상자 기능 ④ 친구 단계, 사용자 결정 2026-10-23, decisions.txt "상자 기능 확정" 위 문단): 상자 고치기는 호스트 한 곳에서만,
    /// 손님은 옮기기를 호스트에 부탁한다. 손님 화면은 먼저 옮겨진 것으로 보여 주고(예측) 답이 다르면 그만큼만 바로잡는다.
    /// 물건 = 키(이름·품질·변형을 Plugin이 한 문자열로)와 개수. 같은 순간 복제는 감수, 손실은 막음.
    /// </summary>
    /// <summary>넣기 자리 하나: 칸 번호와 넣을 수(튜플은 게임 런타임에 System.ValueTuple이 없어 안 씀).</summary>
    public readonly struct PutStep
    {
        public readonly int Slot, N;
        public PutStep(int slot, int n) { Slot = slot; N = n; }
    }

    public static class ChestDeal
    {
        /// <summary>호스트: 손님이 칸에서 want개 꺼내 가려 함. 그 칸에 같은 물건이 있으면 있는 만큼(최대 want), 다르거나 비었으면 0.</summary>
        public static int Take(string slotKey, int slotAmount, string wantKey, int want)
        {
            if (want <= 0 || slotAmount <= 0 || slotKey == null || slotKey != wantKey) return 0;
            return want < slotAmount ? want : slotAmount;
        }

        /// <summary>
        /// 호스트: 손님이 보낸 물건 amount개를 넣을 자리. keys[i]·amounts[i] = 칸 i 내용(빈칸은 null·0), maxStack = 그 물건 한 칸 최대.
        /// 순서: 손님이 놓은 칸(preferred, 비었거나 같은 물건이고 자리 있으면) → 같은 물건 칸 → 빈칸(앞 번호부터). 남은 수는 leftover(손님에게 돌려줌).
        /// </summary>
        public static List<PutStep> Put(string[] keys, int[] amounts, int maxStack, int preferred, string key, int amount, out int leftover)
        {
            var plan = new List<PutStep>();
            int left = amount;
            if (amount <= 0 || maxStack <= 0 || key == null) { leftover = amount < 0 ? 0 : amount; return plan; }
            var used = new bool[keys.Length];
            void Fill(int i)
            {
                if (left <= 0 || i < 0 || i >= keys.Length || used[i]) return;
                int room = keys[i] == null || amounts[i] <= 0 ? maxStack : keys[i] == key ? maxStack - amounts[i] : 0;
                if (room <= 0) return;
                int n = room < left ? room : left;
                plan.Add(new PutStep(i, n)); used[i] = true; left -= n;
            }
            Fill(preferred);
            for (int i = 0; i < keys.Length; i++) if (keys[i] == key && amounts[i] > 0) Fill(i);
            for (int i = 0; i < keys.Length; i++) if (keys[i] == null || amounts[i] <= 0) Fill(i);
            leftover = left;
            return plan;
        }
    }

    /// <summary>상자 칸 하나의 꺼내기·넣기(손님 화면의 옮기기 전후 차이).</summary>
    public readonly struct SlotOp
    {
        public readonly int Slot, N;
        public readonly string Key;
        public SlotOp(int slot, string key, int n) { Slot = slot; Key = key; N = n; }
    }

    public static class ChestDiff
    {
        /// <summary>
        /// 손님 쪽: 옮기기 전(bk·ba)과 뒤(ak·aa) 상자 칸 내용 차이 → 꺼내기(takes)·넣기(puts). 같은 물건이 줄면 꺼내기, 늘면 넣기,
        /// 다른 물건으로 바뀌면 옛것 다 꺼내고 새것 다 넣기. 호스트는 꺼내기를 모두 한 뒤 넣기를 한다(칸 바꾸기·상자 안 옮기기도 같은 꼴).
        /// </summary>
        public static void Diff(string[] bk, int[] ba, string[] ak, int[] aa, List<SlotOp> takes, List<SlotOp> puts)
        {
            int n = bk.Length > ak.Length ? bk.Length : ak.Length;
            for (int i = 0; i < n; i++)
            {
                string b = i < bk.Length && ba[i] > 0 ? bk[i] : null, a = i < ak.Length && aa[i] > 0 ? ak[i] : null;
                int bn = b != null ? ba[i] : 0, an = a != null ? aa[i] : 0;
                if (b == a)
                {
                    if (bn > an) takes.Add(new SlotOp(i, b, bn - an));
                    else if (an > bn) puts.Add(new SlotOp(i, a, an - bn));
                    continue;
                }
                if (b != null) takes.Add(new SlotOp(i, b, bn));
                if (a != null) puts.Add(new SlotOp(i, a, an));
            }
        }

        /// <summary>
        /// 호스트: 한 부탁 안에서 그 물건을 더 넣어도 되는 수 = 이번에 실제로 꺼낸 수 + 손님 가방에서 보낸 수 - 이미 넣은 수.
        /// 꺼내기가 실패한 상자 안 옮기기가 물건을 새로 만들지 않게(복제 막기).
        /// </summary>
        public static int PutAllowed(int taken, int fromBag, int alreadyPut)
        {
            int n = taken + (fromBag > 0 ? fromBag : 0) - alreadyPut;
            return n > 0 ? n : 0;
        }
    }

    /// <summary>
    /// 손님 쪽 답 기다리는 부탁 장부. 부탁 하나 = 화면에서 한 번 옮긴 것(여러 칸). 물건마다 가방 변화(+ 꺼내 옴, - 보냄)를 적어 둔다.
    /// 답 = 물건마다 상자가 실제로 변한 수. 예상(= -가방 변화)과 다르면 그 차이만큼 가방을 고친다(+ 돌려받음, - 뺌).
    /// 답 오기 전 꺼내 온 물건은 버리기·쓰기·입기·제작 재료로 못 씀(Locked). 시간 넘김(접속 끊김 정도)이면 꺼낸 것은 두고 보낸 것은 돌려받는다 — 손실보다 복제 쪽.
    /// </summary>
    public sealed class GuestLedger
    {
        sealed class Pending { public Dictionary<string, int> Bag; public float Since; }

        readonly Dictionary<int, Pending> m_pending = new Dictionary<int, Pending>();
        int m_next = 1;

        public int Count => m_pending.Count;

        /// <summary>옮기기 시작(화면은 이미 옮겨진 것으로). bag = 물건마다 가방 변화(0은 빼도 됨). 부탁 번호를 돌려줌.</summary>
        public int Begin(IDictionary<string, int> bag, float now)
        {
            int id = m_next++;
            if (m_next <= 0) m_next = 1;
            var copy = new Dictionary<string, int>();
            foreach (var kv in bag) if (kv.Value != 0 && kv.Key != null) copy[kv.Key] = kv.Value;
            m_pending[id] = new Pending { Bag = copy, Since = now };
            return id;
        }

        /// <summary>
        /// 호스트 답: chest = 물건마다 상자가 실제로 변한 수(+ 들어감, - 나감). 물건마다 가방에 더할 수(음수면 뺄 수)를 돌려줌, 0은 안 넣음.
        /// 모르는 번호(이미 답함·시간 넘김)면 null.
        /// </summary>
        public Dictionary<string, int> Reply(int id, IDictionary<string, int> chest)
        {
            if (!m_pending.TryGetValue(id, out var p)) return null;
            m_pending.Remove(id);
            var fix = new Dictionary<string, int>();
            var keys = new HashSet<string>(p.Bag.Keys);
            foreach (var k in chest.Keys) if (k != null) keys.Add(k);
            foreach (var k in keys)
            {
                p.Bag.TryGetValue(k, out int bag);
                chest.TryGetValue(k, out int got);
                int d = -bag - got;
                if (d != 0) fix[k] = d;
            }
            return fix;
        }

        /// <summary>답을 기다리며 가방에 꺼내 온 그 물건 수(보낸 것은 이미 가방에 없음) — 이만큼은 버리기·쓰기·입기·제작 재료로 못 씀.</summary>
        public int Locked(string key)
        {
            int n = 0;
            foreach (var p in m_pending.Values) if (p.Bag.TryGetValue(key, out int d) && d > 0) n += d;
            return n;
        }

        /// <summary>
        /// timeout초 넘게 답이 없는 부탁을 지움. 꺼내 온 것은 그대로 두고, 보낸 것은 돌려받음(back에 키·수) — 손실보다 복제 쪽. 지운 수.
        /// </summary>
        public int Expire(float now, float timeout, List<PutStepBack> back)
        {
            var old = new List<int>();
            foreach (var kv in m_pending) if (now - kv.Value.Since > timeout) old.Add(kv.Key);
            foreach (int id in old)
            {
                if (back != null)
                    foreach (var kv in m_pending[id].Bag) if (kv.Value < 0) back.Add(new PutStepBack(kv.Key, -kv.Value));
                m_pending.Remove(id);
            }
            return old.Count;
        }
    }

    /// <summary>시간 넘김으로 손님 가방에 돌려받을 물건.</summary>
    public readonly struct PutStepBack
    {
        public readonly string Key;
        public readonly int N;
        public PutStepBack(string key, int n) { Key = key; N = n; }
    }
}
