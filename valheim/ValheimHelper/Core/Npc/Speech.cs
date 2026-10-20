using System;
using System.Collections.Generic;

namespace ValheimHelper.Core.Npc
{
    /// <summary>1호 = SCV 성향(시가 문 베테랑 일꾼), 2호 = 알라라크 성향 (구상서 10-1장).</summary>
    public enum Personality { Scv = 0, Alarak = 1 }

    /// <summary>
    /// 상황별 대사 고르기. 원작 대사는 쓰지 않는다. 최근에 쓴 줄은 피한다 (10-1장).
    /// 자리표시자: {player} {state} {hp} {max} …, 조사는 {item|는}처럼 쓴다(Josa).
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
                ["moveBase"] = new[] { "새 거점이구먼. 짐 싸서 갑니다요.", "이사요? 허리 나가겠네... 갑니다." },
                ["lost"] = new[] { "어디 가셨수? 일단 집에 가 있을게요.", "놓쳤네... 집에서 기다릴게요." },
                ["status"] = new[] { "지금 {state}. 체력 {hp}/{max}이요." },
                ["notYet"] = new[] { "그건 아직 손에 안 익었수다. 조금만 기다려 보쇼.", "아직 그 일은 배우는 중이라... 다음에 해 드릴게요." },
                ["onlyBang"] = new[] { "말로 하면 아직 못 알아들어요. ! 명령으로 해 주쇼." },
                ["state.idle"] = new[] { "놀고 있수다" },
                ["state.follow"] = new[] { "{player} 따라가는 중" },
                ["state.home"] = new[] { "집에 가는 중" },
                ["state.work"] = new[] { "{item} 모으는 중({done}/{n})" },
                ["gatherStart"] = new[] { "{item} {n}? 갑니다요.", "{item} {n}개, 알겠수다. 다녀옵니다." },
                ["gatherKnown"] = new[] { "{item}, 아는 데 있수다. 금방 다녀옵니다.", "{item} {n}? 거기 가면 있지. 갑니다요." },
                ["gatherDone"] = new[] { "끝났수다. {item} {n}, 상자에 넣어 놨어요.", "{item} {n}개 상자에 넣었수다. 다음은요?" },
                ["dropJob"] = new[] { "하던 건 접고 이거부터 합니다요." },
                ["searching"] = new[] { "{item|요}? 어디 있는지 모르는데... 찾아볼게요.", "{item|는} 처음이라 좀 헤매겠수다." },
                ["noTool"] = new[] { "연장이 없어요. 장비 상자에 하나 넣어 주쇼.", "맨손으로 하라고요? 연장 좀 주쇼." },
                ["toolTooWeak"] = new[] { "이 연장으론 안 되는 놈이 있네. 더 좋은 거 있으면 주쇼." },
                ["toolBroke"] = new[] { "아이고, 연장 나갔네. 예비 있으면 장비 상자에 넣어 주쇼." },
                ["notFound"] = new[] { "{item} 못 찾았어요. 어디 있는지 알려 주시면 갈게요." },
                ["noChest"] = new[] { "넣을 상자가 없어요. 깃발 근처에 상자 하나 놔 주쇼." },
                ["chestFull"] = new[] { "상자가 꽉 찼어요. 남은 건 들고 있을게요." },
                ["chestBusy"] = new[] { "누가 상자를 계속 열고 있네... 나중에 넣을게요." },
                ["chestSet"] = new[] { "{kind} 상자, 알겠수다." },
                ["chestNone"] = new[] { "어느 상자요? 상자를 바라보면서 말해 주쇼." },
                ["chestFar"] = new[] { "그 상자는 깃발에서 너무 멀어요. 20m 안에 있는 걸로 해 주쇼." },
            },
            new Dictionary<string, string[]> // Alarak
            {
                ["spawn"] = new[] { "부름에 응해 주지. 영광으로 알아라.", "내가 왔다. 쓸모 있는 일을 내놓아라." },
                ["escort"] = new[] { "따라가 주지. 길이나 제대로 찾아라.", "앞장서라. 뒤처지는 건 네 쪽일 테니." },
                ["stop"] = new[] { "흥, 돌아가겠다.", "물러나 주지. 오늘만이다." },
                ["home"] = new[] { "돌아왔다. 다음 명령은 좀 더 가치 있길 바란다." },
                ["moveBase"] = new[] { "새 거처라. 나쁘지 않군.", "거처를 옮기는군. 따라가 주지." },
                ["lost"] = new[] { "사라졌군. 기다려 줄 생각은 없다. 돌아가겠다." },
                ["status"] = new[] { "{state}. 체력 {hp}/{max}. 걱정할 것 없다." },
                ["notYet"] = new[] { "그 일은 아직 내 몫이 아니다. 때가 되면 하지." },
                ["onlyBang"] = new[] { "장황한 말은 듣지 않는다. ! 명령으로 해라." },
                ["state.idle"] = new[] { "대기 중이다" },
                ["state.follow"] = new[] { "{player}의 뒤를 봐 주는 중" },
                ["state.home"] = new[] { "돌아가는 중" },
                ["state.work"] = new[] { "{item} 수집 중({done}/{n})" },
                ["gatherStart"] = new[] { "{item} {n|이라}. 쉬운 일이군.", "{item} {n}. 기다려라." },
                ["gatherKnown"] = new[] { "{item|이} 어디 있는지는 이미 안다. 곧 돌아오지." },
                ["gatherDone"] = new[] { "끝났다. {item} {n}, 상자에 있다. 감사 인사는 필요 없다." },
                ["dropJob"] = new[] { "하던 일은 버리지. 네 변덕에 맞춰 주마." },
                ["searching"] = new[] { "{item} 따위 어디 있는지는 모르나, 곧 알게 되겠지." },
                ["noTool"] = new[] { "연장도 없이 일을 시키나? 장비 상자에 넣어 둬라." },
                ["toolTooWeak"] = new[] { "이 하찮은 연장으로는 안 되는 것이 있군. 더 나은 걸 가져와라." },
                ["toolBroke"] = new[] { "연장이 부서졌다. 이런 조잡한 걸 줬으니 당연하지." },
                ["notFound"] = new[] { "{item|은} 이 근방에 없다. 어디 있는지 알면 말해라." },
                ["noChest"] = new[] { "넣을 상자가 없군. 깃발 곁에 하나 놓아라." },
                ["chestFull"] = new[] { "상자가 가득 찼다. 나머지는 내가 들고 있지." },
                ["chestBusy"] = new[] { "누가 상자를 붙잡고 있군. 나중에 넣겠다." },
                ["chestSet"] = new[] { "{kind} 상자라. 기억해 두지." },
                ["chestNone"] = new[] { "어느 상자 말이냐? 상자를 보고 말해라." },
                ["chestFar"] = new[] { "깃발에서 너무 먼 상자다. 20m 안의 것으로 해라." },
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
                foreach (var kv in args)
                {
                    line = WithJosa(line, kv.Key, kv.Value);
                    line = line.Replace("{" + kv.Key + "}", kv.Value);
                }
            return line;
        }

        /// <summary>"{key|는}" → 값 + 받침에 맞는 조사.</summary>
        static string WithJosa(string line, string key, string value)
        {
            string open = "{" + key + "|";
            int i;
            while ((i = line.IndexOf(open, StringComparison.Ordinal)) >= 0)
            {
                int end = line.IndexOf('}', i);
                if (end < 0) break;
                string josa = line.Substring(i + open.Length, end - i - open.Length);
                line = line.Substring(0, i) + value + Josa.For(value, josa) + line.Substring(end + 1);
            }
            return line;
        }
    }
}
