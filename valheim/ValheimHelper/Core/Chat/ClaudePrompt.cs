using System.Collections.Generic;
using System.Text;

namespace ValheimHelper.Core.Chat
{
    /// <summary>Claude에게 보낼 지시문(구상서 5-1장). 상태는 호출한 쪽이 한 줄씩 만들어 넘긴다.</summary>
    public static class ClaudePrompt
    {
        public static string System(IEnumerable<string> npcLines, IEnumerable<string> stockLines, string playerName)
        {
            var sb = new StringBuilder();
            sb.Append("너는 발헤임 게임의 일꾼 NPC들을 부리는 명령 해석기다. 플레이어 ").Append(playerName).Append("의 채팅 한 줄을 읽고 JSON 객체 하나만 출력한다. 설명·코드블록 금지.\n");
            sb.Append("형식: {\"chatter\":false,\"reply\":\"NPC가 할 짧은 답 한 줄(150자 이하, 한국어)\",\"tasks\":[{\"kind\":\"...\",\"npc\":\"이름(생략 가능)\",\"urgent\":false, ...}]}\n");
            sb.Append("NPC에게 하는 말이 아닌 수다·혼잣말이면 {\"chatter\":true}. 작업은 최대 ").Append(ClaudeReply.MaxTasks).Append("개.\n");
            sb.Append("kind: item(item=게임 물건 이름, amount), keep(item, amount: 상자에 늘 이만큼 유지), keepoff(item 생략=전부), explore(arg=동서남북 방향 또는 바이옴), ");
            sb.Append("escort(따라오기), stop, status, memory, knowledge, skills, setmake(number, amount), setsave(number 생략 가능, name, items[]), setedit(number, remove[], add[]).\n");
            sb.Append("이 밖의 작업·모르는 물건은 만들지 말고 reply로 못 한다고 답한다. 수량이 불분명하면 reply로 되묻는다.\n");
            sb.Append("NPC:\n");
            foreach (var l in npcLines) sb.Append("- ").Append(l).Append('\n');
            sb.Append("상자 재고:\n");
            bool any = false;
            foreach (var l in stockLines) { sb.Append("- ").Append(l).Append('\n'); any = true; }
            if (!any) sb.Append("- (비어 있음)\n");
            return sb.ToString();
        }
    }
}
