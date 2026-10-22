namespace ValheimHelper.Core.Npc
{
    public enum EscortAct { Go, Wait, GiveUp }

    /// <summary>
    /// 동행 앞장서기(구상서 8-1, 사용자 순서 9 C): 같이 가자고 한 사람을 기다렸다가 앞장서고, 20m 안을 유지한다.
    /// 일꾼이 일(GatherJob 등)을 하는 동안 매 프레임 Tick → Go면 일을 계속, Wait면 그 자리에서 기다림, GiveUp이면 혼자 감.
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

        public enum Phase { Waiting, Leading, Paused, Alone }

        public Phase Now { get; private set; } = Phase.Waiting;
        public string Say { get; private set; }
        float m_waited;
        float m_sayIn;
        bool m_saidWait;

        /// <param name="distance">동행자까지 거리(m). 동행자가 없으면(나감·죽음) 음수.</param>
        public EscortAct Tick(float dt, float distance)
        {
            Say = null;
            if (Now == Phase.Alone) return EscortAct.Go;
            if (distance < 0f || distance > Gone) return GiveUp();
            m_sayIn -= dt;
            switch (Now)
            {
                case Phase.Waiting:
                    if (distance <= Near) { Now = Phase.Leading; m_waited = 0f; return EscortAct.Go; }
                    m_waited += dt;
                    if (m_waited > StartWait) return GiveUp();
                    if (!m_saidWait) { m_saidWait = true; Say = "escort.wait"; }
                    return EscortAct.Wait;
                case Phase.Leading:
                    if (distance <= Far) return EscortAct.Go;
                    Now = Phase.Paused;
                    if (m_sayIn <= 0f) { m_sayIn = SayEvery; Say = "escort.far"; }
                    return EscortAct.Wait;
                default: // Paused
                    if (distance <= Resume) { Now = Phase.Leading; return EscortAct.Go; }
                    m_waited += dt;
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
