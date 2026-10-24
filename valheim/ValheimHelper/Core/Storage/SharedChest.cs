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

    /// <summary>
    /// 손님 쪽 답 기다리는 옮기기 장부. 답 오기 전 그 물건은 가방 안 이동만 되고 버리기·쓰기·입기·제작 재료는 막는다(Locked).
    /// 답(Reply)이 예측과 다르면 가방을 그 차이만큼 고친다. 시간 넘김(답이 끊김 = 접속 끊김 정도)이면 가방은 그대로 둔다 — 손실보다 복제 쪽.
    /// </summary>
    public sealed class GuestLedger
    {
        sealed class Pending { public bool Put; public string Key; public int Amount; public float Since; }

        readonly Dictionary<int, Pending> m_pending = new Dictionary<int, Pending>();
        int m_next = 1;

        public int Count => m_pending.Count;

        /// <summary>옮기기 시작(화면은 이미 옮겨진 것으로). put = 가방 → 상자, 아니면 상자 → 가방. 부탁 번호를 돌려줌.</summary>
        public int Begin(bool put, string key, int amount, float now)
        {
            int id = m_next++;
            if (m_next <= 0) m_next = 1;
            m_pending[id] = new Pending { Put = put, Key = key, Amount = amount, Since = now };
            return id;
        }

        /// <summary>
        /// 호스트 답: done = 실제로 옮겨진 수. 가방에 더할 수(음수면 뺄 수)를 돌려줌 — 꺼내기가 덜 되면 덜 된 만큼 빼고, 넣기가 덜 되면 돌려받음.
        /// 모르는 번호(이미 답함·시간 넘김)면 0.
        /// </summary>
        public int Reply(int id, int done, out string key)
        {
            key = null;
            if (!m_pending.TryGetValue(id, out var p)) return 0;
            m_pending.Remove(id);
            key = p.Key;
            if (done < 0) done = 0;
            if (done > p.Amount) done = p.Amount;
            int miss = p.Amount - done;
            return p.Put ? miss : -miss;
        }

        /// <summary>답을 기다리며 가방에 꺼내 온 그 물건 수(보내려고 뺀 것은 이미 가방에 없음) — 이만큼은 버리기·쓰기·입기·제작 재료로 못 씀.</summary>
        public int Locked(string key)
        {
            int n = 0;
            foreach (var p in m_pending.Values) if (!p.Put && p.Key == key) n += p.Amount;
            return n;
        }

        /// <summary>timeout초 넘게 답이 없는 부탁을 지움(가방은 그대로). 지운 수.</summary>
        public int Expire(float now, float timeout)
        {
            var old = new List<int>();
            foreach (var kv in m_pending) if (now - kv.Value.Since > timeout) old.Add(kv.Key);
            foreach (int id in old) m_pending.Remove(id);
            return old.Count;
        }
    }
}
