namespace ValheimHelper.Core.Chat
{
    /// <summary>채팅 한 줄을 누가 처리하는지 (구상서 5장).</summary>
    public enum ChatRoute
    {
        /// <summary>처리하지 않음: 빈 줄, 2글자 이하, 웃음·이모티콘뿐인 줄.</summary>
        Ignore,
        /// <summary>정해진 "!" 명령. 모드가 바로 처리한다.</summary>
        Command,
        /// <summary>질문형 반응 뒤의 짧은 대답(응/아니). 모드가 처리한다 (8-1장).</summary>
        Answer,
        /// <summary>"!"는 있지만 형식이 다름 → Haiku.</summary>
        Haiku,
        /// <summary>"!" 없는 채팅 → Sonnet.</summary>
        Sonnet,
    }

    public enum CommandKind
    {
        /// <summary>!나무 50, !구리주괴 20 — 채집인지 제작인지는 계획 단계가 정한다 (6-1장).</summary>
        Item,
        Status,
        Skills,
        Memory,
        Knowledge,
        /// <summary>!탐험 [방향|바이옴]. Arg = 방향("east" 등)·바이옴 이름, 없으면 null.</summary>
        Explore,
        Escort,
        GoAnyway,
        Stop,
        ClearQueue,
        /// <summary>!상자 &lt;종류&gt; [NPC]. Arg = "equipment" | "food" | "seeds" | 물건 이름.</summary>
        SetChest,
        /// <summary>!채워 &lt;물건&gt; &lt;수량&gt;.</summary>
        Keep,
        /// <summary>!채워 세트 &lt;번호&gt; &lt;수량&gt;.</summary>
        KeepSet,
        KeepOff,
        /// <summary>!세트 — 목록 보기.</summary>
        SetList,
        /// <summary>!세트 &lt;번호&gt; &lt;수량&gt;.</summary>
        SetMake,
        /// <summary>세트 저장(Claude가 말에서 뽑음, 6-4장). Number 0이면 다음 빈 번호.</summary>
        SetSave,
        /// <summary>세트 고치기(Claude가 말에서 뽑음).</summary>
        SetEdit,
        /// <summary>!밭 &lt;반경&gt;. Number = 반경(m).</summary>
        Farm,
        /// <summary>!평탄화 &lt;반경&gt;. Number = 반경(m).</summary>
        Level,
        Path,
    }

    /// <summary>질문 뒤 짧은 대답 (8-1장, 설계 B-7).</summary>
    public enum AnswerKind { Yes, No, GoAnyway, Escort, Elsewhere }

    /// <summary>해석된 정해진 명령.</summary>
    public sealed class ChatCommand
    {
        public CommandKind Kind;
        /// <summary>"!플로키 철 30"처럼 이름으로 부른 NPC. 설정의 이름 그대로. 없으면 null.</summary>
        public string Target;
        /// <summary>Item·Keep·SetChest(자원 상자)의 물건의 게임 프리팹 이름. 모르는 이름이면 명령이 아니라 Haiku로 간다.</summary>
        public string Item;
        /// <summary>Item·Keep·KeepSet·SetMake의 수량.</summary>
        public int Amount;
        /// <summary>세트 번호, 밭·평탄화 반경.</summary>
        public int Number;
        /// <summary>Explore: 방향("north", "northeast" …). SetChest: "equipment" | "food" | "seeds" | "item".</summary>
        public string Arg;
        /// <summary>Explore: 게임 바이옴 이름(Heightmap.Biome, 예: "BlackForest").</summary>
        public string Biome;
        /// <summary>수량·반경이 상한을 넘어 줄였는지. 알림에 쓴다.</summary>
        public bool Clamped;
        /// <summary>"빨리·먼저·이거부터" — 하던 일을 멈추고 이것부터(설계 B-5).</summary>
        public bool Urgent;
    }

    public sealed class ChatResult
    {
        public ChatRoute Route;
        /// <summary>Route == Command일 때.</summary>
        public ChatCommand Command;
        /// <summary>Route == Answer일 때.</summary>
        public AnswerKind Answer;
        /// <summary>Haiku·Sonnet으로 보낼 원문(앞뒤 공백 제거).</summary>
        public string Text;
        /// <summary>Haiku·Sonnet 전에 이름으로 부른 NPC (예: "Claude야 나무 좀"). 없으면 null.</summary>
        public string Target;
    }
}
