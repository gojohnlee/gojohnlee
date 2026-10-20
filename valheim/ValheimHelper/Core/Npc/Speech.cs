using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Npc
{
    /// <summary>1호 = SCV 성향(시가 문 베테랑 일꾼), 2호 = 알라라크 성향 (구상서 10-1장).</summary>
    public enum Personality { Scv = 0, Alarak = 1 }

    /// <summary>
    /// 상황별 대사 고르기. 원작 대사는 쓰지 않는다. 최근에 쓴 줄은 피한다 (10-1장).
    /// 자리표시자: {player} {state} {hp} {max}.
    /// TODO: 대사가 늘면 코드 밖 텍스트 파일로 옮긴다 (12장).
    /// </summary>
    public sealed class Speech
    {
        static readonly Dictionary<string, string[]>[] Lines =
        {
            new Dictionary<string, string[]> // Scv
            {
                ["spawn"] = new[] { "...어디 보자, 새 현장이구먼. 일 주쇼.", "출근했수다. 뭐부터 할까요?" },
                ["escort"] = new[] { "예예, 따라갑니다요.", "앞장서쇼. 뒤는 내가 봐 드릴 테니.", "같이 가요. 너무 빨리 뛰진 마시고." },
                ["stop"] = new[] { "알겠수다. 집에 가 있을게요.", "예, 철수합니다. 깃발 앞에 있을게요." },
                ["home"] = new[] { "도착. 담배나 한 대 피워야겠네.", "집 왔수다. 부르면 나갑니다." },
                ["lost"] = new[] { "어디 가셨수? 일단 집에 가 있을게요.", "놓쳤네... 집에서 기다릴게요." },
                ["status"] = new[] { "지금 {state}. 체력 {hp}/{max}이요." },
                ["notYet"] = new[] { "그건 아직 손에 안 익었수다. 조금만 기다려 보쇼.", "아직 그 일은 배우는 중이라... 다음에 해 드릴게요." },
                ["onlyBang"] = new[] { "말로 하면 아직 못 알아들어요. ! 명령으로 해 주쇼." },
                ["state.idle"] = new[] { "놀고 있수다" },
                ["state.follow"] = new[] { "{player} 따라가는 중" },
                ["state.home"] = new[] { "집에 가는 중" },
            },
            new Dictionary<string, string[]> // Alarak
            {
                ["spawn"] = new[] { "부름에 응해 주지. 영광으로 알아라.", "내가 왔다. 쓸모 있는 일을 내놓아라." },
                ["escort"] = new[] { "따라가 주지. 길이나 제대로 찾아라.", "앞장서라. 뒤처지는 건 네 쪽일 테니." },
                ["stop"] = new[] { "흥, 돌아가겠다.", "물러나 주지. 오늘만이다." },
                ["home"] = new[] { "돌아왔다. 다음 명령은 좀 더 가치 있길 바란다." },
                ["lost"] = new[] { "사라졌군. 기다려 줄 생각은 없다. 돌아가겠다." },
                ["status"] = new[] { "{state}. 체력 {hp}/{max}. 걱정할 것 없다." },
                ["notYet"] = new[] { "그 일은 아직 내 몫이 아니다. 때가 되면 하지." },
                ["onlyBang"] = new[] { "장황한 말은 듣지 않는다. ! 명령으로 해라." },
                ["state.idle"] = new[] { "대기 중이다" },
                ["state.follow"] = new[] { "{player}의 뒤를 봐 주는 중" },
                ["state.home"] = new[] { "돌아가는 중" },
            },
        };

        readonly Random _rng;
        readonly Dictionary<string, string> _last = new Dictionary<string, string>();

        public Speech(Random rng = null) { _rng = rng ?? new Random(); }

        public static bool Has(Personality p, string key) => Lines[(int)p].ContainsKey(key);

        /// <summary>대사 하나. 같은 상황에서 바로 전에 쓴 줄은 피한다. 없는 키면 키 이름을 그대로 돌려준다.</summary>
        public string Say(Personality p, string key, IDictionary<string, string> args = null)
        {
            if (!Lines[(int)p].TryGetValue(key, out var options) || options.Length == 0) return key;
            string memo = (int)p + "/" + key;
            _last.TryGetValue(memo, out var last);
            string line;
            if (options.Length == 1) line = options[0];
            else
            {
                int i = _rng.Next(options.Length - (last != null ? 1 : 0));
                line = options[i];
                if (line == last) line = options[options.Length - 1];
            }
            _last[memo] = line;
            if (args != null)
                foreach (var kv in args) line = line.Replace("{" + kv.Key + "}", kv.Value);
            return line;
        }
    }
}
