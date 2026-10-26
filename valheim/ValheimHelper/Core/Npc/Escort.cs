namespace ValheimHelper.Core.Npc
{
    public enum EscortAct { Go, Wait, GiveUp }

    /// <summary>
    /// 동행(일꾼 + 호위병 — 사용자 결정 2026-10-26): 앞장설 일꾼이 호위병을 기다렸다가 앞장서고, 20m 안을 유지한다.
    /// 일꾼이 일(GatherJob 등)을 하는 동안 매 프레임 Tick → Go면 일을 계속, Wait면 그 자리에서 기다림, GiveUp이면 혼자 감.
    /// 동료는 거리와 상관없이 부르므로 출발 전 기다림은 처음 거리에 맞춰 늘고(ForMate), 오는 동안은 200m 밖이어도 포기하지 않는다.
    /// Say는 이번에 할 말(Speech 키) 또는 null.
    /// </summary>
    public sealed class Escort
    {
        /// <summary>출발 전: 이만큼 가까이 오면 앞장선다.</summary>
        public const float Near = 8f;
        /// <summary>이보다 멀어지면 멈춰서 기다린다(구상서 20m).</summary>
        public const float Far = 20f;
        /// <summary>멈춘 뒤 이만큼 가까워지면 다시 간다(멈춤·출발 반복 막기).</summary>
        public const float Resume = 12f;
        /// <summary>이보다 멀면(포탈 등) 바로 혼자 간다.</summary>
        public const float Gone = 200f;
        /// <summary>출발 전 기다리는 한도(초).</summary>
        public const float StartWait = 60f;
        /// <summary>가다가 멈춰 기다린 시간 합 한도(초).</summary>
        public const float LostWait = 90f;
        /// <summary>"어디 가요?!" 다시 말하는 간격(초).</summary>
        public const float SayEvery = 30f;

        /// <summary>동료가 오는 속도로 잡은 값(m/s, 일꾼 달리기 7보다 느리게 — 돌아가는 길).</summary>
        public const float MateSpeed = 4f;

        public enum Phase { Waiting, Leading, Paused, Alone }

        readonly float m_startWait;

        public Escort(float startWait = StartWait) { m_startWait = startWait; }

        /// <summary>동료 일꾼과 같이 갈 때: 처음 거리만큼 기다림을 늘린다(최소 StartWait).</summary>
        public static Escort ForMate(float distance) => new Escort(System.Math.Max(StartWait, distance / MateSpeed + 30f));

        public Phase Now { get; private set; } = Phase.Waiting;
        public string Say { get; private set; }
        /// <summary>이번에 멈춰 기다린 지 몇 초째(Paused일 때만, 아니면 0) — 오래 못 따라오면 플러그인이 동행자를 곁으로 옮긴다.</summary>
        public float PausedFor { get; private set; }
        float m_waited;
        float m_sayIn;
        bool m_saidWait;

        /// <param name="distance">동행자까지 거리(m). 동행자가 없으면(나감·죽음) 음수.</param>
        public EscortAct Tick(float dt, float distance)
        {
            Say = null;
            if (Now != Phase.Paused) PausedFor = 0f;
            if (Now == Phase.Alone) return EscortAct.Go;
            if (distance < 0f || (distance > Gone && Now != Phase.Waiting)) return GiveUp();
            m_sayIn -= dt;
            switch (Now)
            {
                case Phase.Waiting:
                    if (distance <= Near) { Now = Phase.Leading; m_waited = 0f; return EscortAct.Go; }
                    m_waited += dt;
                    if (m_waited > m_startWait) return GiveUp();
                    if (!m_saidWait) { m_saidWait = true; Say = "escort.wait"; }
                    return EscortAct.Wait;
                case Phase.Leading:
                    if (distance <= Far) return EscortAct.Go;
                    Now = Phase.Paused;
                    if (m_sayIn <= 0f) { m_sayIn = SayEvery; Say = "escort.far"; }
                    return EscortAct.Wait;
                default: // Paused
                    if (distance <= Resume) { Now = Phase.Leading; PausedFor = 0f; return EscortAct.Go; }
                    m_waited += dt;
                    PausedFor += dt;
                    if (m_waited > LostWait) return GiveUp();
                    if (m_sayIn <= 0f) { m_sayIn = SayEvery; Say = "escort.far"; }
                    return EscortAct.Wait;
            }
        }

        EscortAct GiveUp()
        {
            Now = Phase.Alone;
            Say = "escort.giveup";
            return EscortAct.GiveUp;
        }
    }
}
