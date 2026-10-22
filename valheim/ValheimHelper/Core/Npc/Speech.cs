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
                ["escort.far"] = new[] { "어디 가요?! 같이 가야죠!", "잠깐만요, 너무 빨라요!" },
                ["raid"] = new[] { "습격이다!! 일은 나중에, 거점으로 돌아갑니다!", "이거 큰일이우! 얼른 돌아가요!" },
                ["stuck"] = new[] { "여기서 꼼짝을 못 하겠수다. 일단 집으로 돌아가요!", "길이 막혔수다. 돌아가서 다음 일 하겠수다!" },
                ["stop"] = new[] { "알겠수다. 집에 가 있을게요.", "예, 철수합니다. 깃발 앞에 있을게요." },
                ["home"] = new[] { "도착. 담배나 한 대 피워야겠네.", "집 왔수다. 부르면 나갑니다." },
                ["moveBase"] = new[] { "새 거점이구먼. 짐 싸서 갑니다요.", "이사요? 허리 나가겠네... 갑니다." },
                ["lost"] = new[] { "어디 가셨수? 일단 집에 가 있을게요.", "놓쳤네... 집에서 기다릴게요." },
                ["status"] = new[] { "지금 {state}. 체력 {hp}/{max}이요." },
                ["notYet"] = new[] { "그건 아직 손에 안 익었수다. 조금만 기다려 보쇼.", "아직 그 일은 배우는 중이라... 다음에 해 드릴게요." },
                ["onlyBang"] = new[] { "말로 하면 아직 못 알아들어요. ! 명령으로 해 주쇼." },
                ["thinking"] = new[] { "음... 잠깐만요.", "생각 좀 해 보겠수다." },
                ["state.idle"] = new[] { "놀고 있수다" },
                ["state.follow"] = new[] { "{player} 따라가는 중" },
                ["state.home"] = new[] { "집에 가는 중" },
                ["state.work"] = new[] { "{item} 모으는 중({done}/{n})" },
                ["gatherStart"] = new[] { "{item} {n}? 갑니다요.", "{item} {n}개, 알겠수다. 다녀옵니다." },
                ["gatherKnown"] = new[] { "{item}, 아는 데 있수다. 금방 다녀옵니다.", "{item} {n}? 거기 가면 있지. 갑니다요." },
                ["gatherDone"] = new[] { "끝났수다. {item} {n}, 상자에 넣어 놨어요.", "{item} {n}개 상자에 넣었수다. 다음은요?" },
                ["dropJob"] = new[] { "하던 건 접고 이거부터 합니다요." },
                ["searching"] = new[] { "{item|요}? 어디 있는지 모르는데... 찾아볼게요.", "{item|는} 처음이라 좀 헤매겠수다." },
                ["noTool"] = new[] { "연장이 없어요. 깃발 근처 상자에 하나 넣어 주쇼.", "맨손으로 하라고요? 연장 좀 주쇼." },
                ["toolTooWeak"] = new[] { "이 연장으론 안 되는 놈이 있네. 더 좋은 거 있으면 주쇼." },
                ["toolBroke"] = new[] { "아이고, 연장 나갔네. 예비 있으면 상자에 넣어 주쇼." },
                ["noFood"] = new[] { "배고픈데... 상자에 먹을 게 없네요. 일단 갑니다.", "먹을 게 없수다. 상자에 좀 넣어 주쇼. 일단 갑니다요." },
                ["fightStart"] = new[] { "{foe}! 붙어 봅니다.", "{foe|이} 오네. 한판 하죠." },
                ["backOff"] = new[] { "{foe}, 혼자선 좀 빡세요. 잠깐 물러날게요.", "어이쿠, {foe|이} 많네. 좀 빠졌다 다시 갈게요." },
                ["flee"] = new[] { "{foe}! 저건 무리예요, 튑니다!", "으악, {foe}! 도망갑니다요!" },
                ["help"] = new[] { "도와줘요!! {foe} 때문에 큰일이우!", "살려 줘요!! 여기 {foe|이} 있어요!" },
                ["dangerSkip"] = new[] { "거긴 {foe} 때문에 안 되겠수다. 다른 데 찾아볼게요." },
                ["drink"] = new[] { "꿀꺽... 미드 한 병 했수다.", "미드 마셨수다. 좀 살겠네." },
                ["resist"] = new[] { "저항 미드 한 병 했수다. 이제 좀 버티겠네.", "꿀꺽, 저항 미드요. 덤벼 보쇼." },
                ["died"] = new[] { "으윽... {by}당했수다. 묘비는 거점에서 {dir} {dist}m, 지도에 찍어 놨어요.", "아이고, 죽었네... {by}당했어요. 묘비는 {dir} {dist}m. 챙기러 갈게요." },
                ["diedNoGrave"] = new[] { "{by}당했수다. 들고 있던 건 없어서 다행이구먼." },
                ["graveGo"] = new[] { "묘비 챙기러 갑니다. {dir} {dist}m요.", "내 짐 찾으러 다녀올게요. {dir}으로 {dist}m." },
                ["graveGot"] = new[] { "묘비 챙겨 왔수다. 다시 일할 수 있어요.", "짐 다 찾았수다. 휴." },
                ["graveGotSome"] = new[] { "다 못 들겠어서 일부만 챙겼수다. 남은 건 묘비에 있어요." },
                ["graveGone"] = new[] { "묘비가 없네요? 누가 챙겨 갔나 봐요." },
                ["graveRetry"] = new[] { "묘비까지 못 가겠어요. 좀 있다 다시 가 볼게요." },
                ["graveFail"] = new[] { "묘비는 포기할게요... 거점에서 {dir} {dist}m에 있어요. 좀 챙겨 주쇼." },
                ["graveNight"] = new[] { "밤이라 묘비는 날 밝으면 챙기러 갈게요." },
                ["state.grave"] = new[] { "묘비 챙기는 중" },
                ["notFound"] = new[] { "{item} 못 찾았어요. 어디 있는지 알려 주시면 갈게요." },
                ["huntHurt"] = new[] { "많이 다쳐서 {item} 사냥은 그만하고 돌아갈게요." },
                ["needBow"] = new[] { "{item|는} 활로만 잡을 수 있어요. 장비 상자에 활이랑 화살 좀 넣어 주쇼." },
                ["noChest"] = new[] { "넣을 상자가 없어요. 깃발 근처에 상자 하나 놔 주쇼." },
                ["chestFull"] = new[] { "상자가 꽉 찼어요. 남은 건 들고 있을게요." },
                ["craftChestFull"] = new[] { "상자가 꽉 찼어요… 상자 하나만 더 만들어 주세요. 만든 건 상자 앞에 뒀수다.", "아이고, 상자에 더 들어갈 데가 없수다. 상자 하나만 더 짜 주쇼, 네?" },
                ["chestBusy"] = new[] { "누가 상자를 계속 열고 있네... 나중에 넣을게요." },
                ["chestAny"] = new[] { "상자는 안 정해도 돼요. 깃발 20m 안 상자는 다 씁니다요." },
                ["bagFull"] = new[] { "짐이 꽉 찼는데 넣을 데가 없어요. 상자 좀 비워 주쇼." },
                ["state.workExplore"] = new[] { "{item} 찾으러 새 땅 도는 중({done}/{n})" },
                ["state.explore"] = new[] { "{where} 탐험 중" },
                ["nearby"] = new[] { "가까운 데" },
                ["exploreStart"] = new[] { "아는 데엔 {item|이} 없어요. 새 땅 좀 둘러보고 올게요.", "{item|이} 아는 데엔 없네... 안 가 본 데 좀 뒤져 볼게요." },
                ["exploreFound"] = new[] { "{item} 찾았수다! 거점에서 {dir} {dist}m쯤이요.", "찾았다, {item}! {dir}으로 {dist}m쯤 가면 있수다." },
                ["exploreGo"] = new[] { "{where} 둘러보고 오겠수다.", "{where|요}? 한 바퀴 돌고 옵니다." },
                ["exploreBiomeUnknown"] = new[] { "{biome|이} 어딘지는 모르겠고... 일단 가까운 데부터 뒤져 볼게요." },
                ["exploreBiomeFound"] = new[] { "{biome} 찾았수다! 거점에서 {dir} {dist}m. 좀 더 둘러볼게요." },
                ["exploreDone"] = new[] { "다녀왔수다. 새로 둘러본 데 {zones}곳{new}.", "정찰 끝. {zones}곳 둘러봤수다{new}." },
                ["exploreNew"] = new[] { ", 처음 본 건 {items}" },
                ["exploreNoMore"] = new[] { "더 갈 데가 없네요. 물에 막혔수다." },
                ["nightBack"] = new[] { "날 저물었수다. 오늘은 여기까지, 돌아갑니다.", "어두워져서 들어갑니다. 밤길은 위험해서요." },
                ["nightNoExplore"] = new[] { "밤엔 모르는 데 안 갑니다. 날 밝으면 가죠." },
                ["knowledge"] = new[] { "아는 거요? {list}{more}. 둘러본 데는 {looked}구역이요." },
                ["knowledgeMore"] = new[] { " 말고도 {n}가지 더" },
                ["knowledgeEmpty"] = new[] { "아직 아는 게 없수다. 일 시키면 다니면서 배울게요." },
                ["memoryEmpty"] = new[] { "아직 혼난 데가 없수다. 위험한 데 생기면 기억해 둘게요." },
                ["memory"] = new[] { "조심할 데요? {list}." },
                ["skillUp"] = new[] { "{skill} {level}. 손에 좀 익네요.", "{skill} {level}! 이제 좀 할 만하구먼." },
                ["skills"] = new[] { "솜씨요? {list}. 나머진 아직 10이요." },
                ["skillsNone"] = new[] { "아직 다 10이요. 일 시키면 늘어요." },
                ["craft.plan"] = new[] { "{item} {n}이면 {short|이} 없어서 {minutes}분쯤 걸려요. 바로 시작하겠수다." },
                ["craft.inStock"] = new[] { "{item} {n}? 상자에 이미 있수다." },
                ["craft.missing"] = new[] { "{item} 만들려면 {station|이} 있어야 돼요. 하나 지어 주쇼." },
                ["craft.tooLong"] = new[] { "{item} {n}은 {minutes}분 넘게 걸려요. 너무 길어요, 나눠서 시켜 주쇼." },
                ["craft.unknown"] = new[] { "{item} 만들 재료 중에 {what|은} 어디서 나는지 모르겠수다." },
                ["setList"] = new[] { "세트메뉴요: {list}." },
                ["setEmpty"] = new[] { "저장된 세트가 없수다. \"세트메뉴 1번은 ...\" 하고 알려 주쇼." },
                ["setNone"] = new[] { "{no}번 세트는 없는데요?" },
                ["setSaved"] = new[] { "{no}번 세트, 적어 뒀수다." },
                ["keepSet"] = new[] { "{item}, {n}개 밑으로 내려가면 채워 둘게요." },
                ["keepOff"] = new[] { "{item} 채우기 그만둘게요." },
                ["farmSet"] = new[] { "여기 반경 {r}m, 밭으로 적어 뒀수다." },
                ["farmMoved"] = new[] { "있던 밭을 여기로 옮겼수다. 반경 {r}m요." },
                ["farmFull"] = new[] { "밭은 이미 여덟 군데나 돼요. 더는 못 봐요." },
                ["farmNoGrow"] = new[] { "여긴 땅이 안 맞아서 심어도 안 자라요. 다른 데 골라 주쇼." },
                ["farmWhere"] = new[] { "어디가 밭인지 모르겠수다. 그 자리에 서서 다시 말해 주쇼." },
                ["farmNoTool"] = new[] { "경작기가 없어요. 장비 상자에 경작기 하나 넣어 주쇼." },
                ["farmNoSeed"] = new[] { "심을 씨앗이 없수다. 상자에 씨앗 좀 넣어 주쇼." },
                ["farmGo"] = new[] { "밭에 {list} 심으러 갑니다요." },
                ["farmDone"] = new[] { "{n}개 심었수다.{more}" },
                ["farmNoRoom"] = new[] { " 밭에 빈자리가 없어 {left}개는 못 심었어요." },
                ["state.farm"] = new[] { "밭에 심는 중({n}/{total})" },
                ["job.farm"] = new[] { "밭일" },
                ["farmAlready"] = new[] { "그 밭은 {npc}가 벌써 보고 있수다." },
                ["farmHarvest"] = new[] { "{list} 거둬 왔수다." },
                ["farmPlantFor"] = new[] { "{item} 없어요. 밭에 심어 둘게요. {minutes}분쯤 걸려요." },
                ["craft.farm"] = new[] { "{item} {n} 만들려면 {what|이} 있어야 해서 밭에 심어 둘게요. {minutes}분쯤 뒤에 다시 시켜 주쇼." },
                ["farmHarvestFor"] = new[] { "밭에 {item} 다 자란 게 있수다. 거둬 올게요." },
                ["farmNoField"] = new[] { "{item}, 밭에서 나는 건데 심을 밭이 없수다. 밭 자리에 서서 !밭 10 해 주쇼." },
                ["farmNoSeedFor"] = new[] { "{item} 심을 씨앗이 없수다. 상자에 씨앗 좀 넣어 주쇼." },
                ["refillGo"] = new[] { "{item|이} 좀 모자라서 {n}개 채우러 갑니다요.", "상자에 {item} 떨어져 가네. {n}개 다녀올게요." },
                ["refillMake"] = new[] { "{item|이} 모자라서 {n}개 만들어 둘게요. {minutes}분쯤 걸려요." },
                ["depart.warn"] = new[] { "{item} {n}, 갑니다. 거긴 {foe|이} 있어서 좀 걸릴 수도 있어요.", "거기 {foe} 나오는 데구먼. 조심해서 다녀올게요." },
                ["depart.hungry"] = new[] { "배고픈데... 일단 갑니다.", "빈속이지만 다녀옵니다요. 음식 좀 채워 주쇼." },
                ["depart.far"] = new[] { "{item} {n}, 멀어서 {minutes}분은 걸려요. 근처에 포탈 하나 놔 주시면 빨라요." },
                ["depart.trips"] = new[] { "한 번에 다 못 들어요. {trips}번 왕복할게요.", "{item} {n}이면 {trips}번은 왔다 갔다 해야겠수다. 갑니다." },
                ["depart.lowSkill"] = new[] { "아직 손이 서툴러서 좀 걸려요. 다녀옵니다.", "솜씨가 아직이라... 천천히 해 올게요." },
                ["depart.busy"] = new[] { "지금 {job}부터 하고 갈게요.", "{job} 끝내고 바로 갑니다요." },
                ["depart.together"] = new[] { "혼자선 빡센 데라 {partner}랑 같이 다녀올게요.", "거긴 둘이 가야 돼요. {partner}, 가자고." },
                ["depart.outOfScope"] = new[] { "그건 제 손으론 못 구해요. 배나 보스 잡을 게 있어야 돼서요." },
                ["depart.noCrypt"] = new[] { "철은 늪 지하묘지에서 나와요. 열쇠로 문 연 곳이 없어서 못 가요. 문 하나만 열어 주세요." },
                ["depart.cryptsEmpty"] = new[] { "아는 지하묘지는 고철을 다 캤어요. 새 지하묘지를 찾아 주셔야 가요." },
                ["cryptOpen"] = new[] { "늪 열쇠 문, 제가 열고 들어갈게요.", "문 열고 들어갑니다요. 고철 캐 올게요." },
                ["ask.alternative"] = new[] { "거기 {foe} 있어서 혼자 빡세요. 대신 {altDir}쪽 {altDist}m는 어때요? 거긴 좀 덜해요.{extra}" },
                ["ask.escort"] = new[] { "지금 거기 가라고요? {foe} 있는 데라 혼자선 무리예요. 엄호 좀!!{extra}", "{foe} 때문에 혼자는 못 가겠수다. 같이 가 주쇼.{extra}" },
                ["ask.diedThere"] = new[] { "저번에 거기서 죽었어요... 같이 가면 갈게요.{extra}" },
                ["ask.condition"] = new[] { "거긴 빠듯해요. 체력 미드 하나만 주시면 갈게요.{extra}" },
                ["depart.noResist"] = new[] { "{mead}도 없이 가라니… 에휴, {item} {n} 다녀올게요.", "{mead} 하나 없이 거길 가라니, 참 내… 그래도 갑니다요, {item} {n}." },
                ["ask.night"] = new[] { "곧 밤이에요. 아침에 갈까요?{extra}", "다녀오면 깜깜하겠는데... 아침에 갈까요?{extra}" },
                ["ask.stock"] = new[] { "상자에 {item} {stock}개 있는데요? 그래도 캐 올까요?" },
                ["note.stock"] = new[] { " 상자에 {stock}개 있긴 해요." },
                ["note.night"] = new[] { " 곧 밤이기도 하고요." },
                ["answer.grumble"] = new[] { "...알겠수다. 묘비 찾으러 와 주셔야 해요.", "에휴, 갑니다. 무슨 일 나면 책임지쇼." },
                ["answer.escort"] = new[] { "좋수다, 같이 가요. 앞장설게요." },
                ["answer.alternative"] = new[] { "그럼 {altDir}쪽으로 갑니다요." },
                ["answer.conditionMet"] = new[] { "미드 확인했수다. 갑니다!" },
                ["answer.conditionMissing"] = new[] { "아직 미드가 안 보이는데요? 상자에 넣으면 바로 갈게요." },
                ["answer.morning"] = new[] { "예, 날 밝으면 갑니다." },
                ["answer.go"] = new[] { "예, 갑니다요." },
                ["answer.hold"] = new[] { "알겠수다. 일단 접어 둘게요." },
                ["answer.cancel"] = new[] { "예, 안 갑니다." },
                ["answer.timeout"] = new[] { "대답이 없으니 일단 접어 둘게요." },
            },
            new Dictionary<string, string[]> // Alarak
            {
                ["spawn"] = new[] { "부름에 응해 주지. 영광으로 알아라.", "내가 왔다. 쓸모 있는 일을 내놓아라." },
                ["escort"] = new[] { "따라가 주지. 길이나 제대로 찾아라.", "앞장서라. 뒤처지는 건 네 쪽일 테니." },
                ["escort.far"] = new[] { "어디로 가는 거지? 멈춰라.", "속도를 맞춰라. 놓칠 뻔했다." },
                ["raid"] = new[] { "적습이다. 일은 접고 돌아간다.", "소란이 크군. 거점으로 물러난다." },
                ["stuck"] = new[] { "발이 묶였다. 거점으로 돌아간다.", "길이 막혔군. 돌아가서 다음 일을 한다." },
                ["stop"] = new[] { "흥, 돌아가겠다.", "물러나 주지. 오늘만이다." },
                ["home"] = new[] { "돌아왔다. 다음 명령은 좀 더 가치 있길 바란다." },
                ["moveBase"] = new[] { "새 거처라. 나쁘지 않군.", "거처를 옮기는군. 따라가 주지." },
                ["lost"] = new[] { "사라졌군. 기다려 줄 생각은 없다. 돌아가겠다." },
                ["status"] = new[] { "{state}. 체력 {hp}/{max}. 걱정할 것 없다." },
                ["notYet"] = new[] { "그 일은 아직 내 몫이 아니다. 때가 되면 하지." },
                ["onlyBang"] = new[] { "장황한 말은 듣지 않는다. ! 명령으로 해라." },
                ["thinking"] = new[] { "...생각 중이다." },
                ["state.idle"] = new[] { "대기 중이다" },
                ["state.follow"] = new[] { "{player}의 뒤를 봐 주는 중" },
                ["state.home"] = new[] { "돌아가는 중" },
                ["state.work"] = new[] { "{item} 수집 중({done}/{n})" },
                ["gatherStart"] = new[] { "{item} {n|이라}. 쉬운 일이군.", "{item} {n}. 기다려라." },
                ["gatherKnown"] = new[] { "{item|이} 어디 있는지는 이미 안다. 곧 돌아오지." },
                ["gatherDone"] = new[] { "끝났다. {item} {n}, 상자에 있다. 감사 인사는 필요 없다." },
                ["dropJob"] = new[] { "하던 일은 버리지. 네 변덕에 맞춰 주마." },
                ["searching"] = new[] { "{item} 따위 어디 있는지는 모르나, 곧 알게 되겠지." },
                ["noTool"] = new[] { "연장도 없이 일을 시키나? 상자에 넣어 둬라." },
                ["toolTooWeak"] = new[] { "이 하찮은 연장으로는 안 되는 것이 있군. 더 나은 걸 가져와라." },
                ["toolBroke"] = new[] { "연장이 부서졌다. 이런 조잡한 걸 줬으니 당연하지." },
                ["noFood"] = new[] { "음식도 없이 일을 시키는군. 상자에 음식을 채워 둬라." },
                ["fightStart"] = new[] { "{foe}... 덤벼라.", "{foe|이} 감히 내 앞을 막는군." },
                ["backOff"] = new[] { "{foe}... 전략적 후퇴다. 잠시 뒤 다시 간다." },
                ["flee"] = new[] { "{foe}... 지금은 상대할 때가 아니다." },
                ["help"] = new[] { "도와라!! {foe}... 네 칼이 필요하다.", "따라와라. {foe}... 혼자선 귀찮은 상대다." },
                ["dangerSkip"] = new[] { "{foe} 때문에 그곳은 버린다. 다른 곳을 찾지." },
                ["drink"] = new[] { "미드를 마신다. 아직 끝나지 않았다." },
                ["resist"] = new[] { "저항 미드를 마셨다. 이 정도로는 날 못 쓰러뜨린다." },
                ["died"] = new[] { "...{by}쓰러졌다. 이번만이다. 묘비는 거점에서 {dir} {dist}m." },
                ["diedNoGrave"] = new[] { "{by}쓰러졌다. 잃은 것은 없다." },
                ["graveGo"] = new[] { "내 물건을 되찾으러 간다. {dir} {dist}m." },
                ["graveGot"] = new[] { "되찾았다. 당연한 일이지." },
                ["graveGotSome"] = new[] { "다 들 수는 없군. 나머지는 묘비에 두었다." },
                ["graveGone"] = new[] { "묘비가 없다. 누가 손을 댔군." },
                ["graveRetry"] = new[] { "묘비에 닿지 못했다. 나중에 다시 간다." },
                ["graveFail"] = new[] { "그 묘비는 버린다. 거점에서 {dir} {dist}m. 원하면 네가 가져와라." },
                ["graveNight"] = new[] { "밤이다. 묘비는 아침에 찾는다." },
                ["state.grave"] = new[] { "묘비 회수 중" },
                ["notFound"] = new[] { "{item|은} 이 근방에 없다. 어디 있는지 알면 말해라." },
                ["huntHurt"] = new[] { "다쳤다. {item} 사냥은 접고 돌아간다." },
                ["needBow"] = new[] { "{item|는} 활 없이는 못 잡는다. 장비 상자에 활과 화살을 넣어라." },
                ["noChest"] = new[] { "넣을 상자가 없군. 깃발 곁에 하나 놓아라." },
                ["chestFull"] = new[] { "상자가 가득 찼다. 나머지는 내가 들고 있지." },
                ["craftChestFull"] = new[] { "상자가 가득 찼다. 상자를 더 만들어라. 만든 것은 상자 앞에 두었다." },
                ["chestBusy"] = new[] { "누가 상자를 붙잡고 있군. 나중에 넣겠다." },
                ["chestAny"] = new[] { "상자를 정할 필요는 없다. 깃발 20m 안의 상자는 다 쓴다." },
                ["bagFull"] = new[] { "짐이 가득한데 둘 곳이 없군. 상자부터 비워라." },
                ["state.workExplore"] = new[] { "{item|을} 찾아 새 땅을 밟는 중({done}/{n})" },
                ["state.explore"] = new[] { "{where} 정찰 중" },
                ["nearby"] = new[] { "가까운 땅" },
                ["exploreStart"] = new[] { "아는 곳엔 {item|이} 없군. 새 땅을 밟아 주지." },
                ["exploreFound"] = new[] { "{item|을} 찾았다. 거점에서 {dir} {dist}m. 당연한 결과지." },
                ["exploreGo"] = new[] { "{where|이라}... 정찰해 주지.", "{where}, 내가 직접 보고 오지." },
                ["exploreBiomeUnknown"] = new[] { "{biome|이} 어디 있든 곧 찾아낸다." },
                ["exploreBiomeFound"] = new[] { "{biome|을} 찾아냈다. 거점에서 {dir} {dist}m. 더 살펴보지." },
                ["exploreDone"] = new[] { "정찰은 끝났다. {zones}곳을 둘러봤다{new}." },
                ["exploreNew"] = new[] { ". 처음 보는 것: {items}" },
                ["exploreNoMore"] = new[] { "더 나아갈 땅이 없다. 물이 길을 막는군." },
                ["nightBack"] = new[] { "밤이다. 어둠 속을 헤맬 이유는 없지. 돌아간다." },
                ["nightNoExplore"] = new[] { "밤에 새 땅이라니. 아침에 하지." },
                ["knowledge"] = new[] { "내가 알아낸 것들이다: {list}{more}. 둘러본 곳 {looked}구역." },
                ["knowledgeMore"] = new[] { " 외 {n}가지" },
                ["knowledgeEmpty"] = new[] { "아직 알아낸 것이 없다. 일을 줘라. 그러면 알게 된다." },
                ["memoryEmpty"] = new[] { "위험한 곳? 아직 나를 겁먹게 한 곳은 없다." },
                ["memory"] = new[] { "경계할 곳이다: {list}." },
                ["skillUp"] = new[] { "{skill} {level}. 당연한 성장이다." },
                ["skills"] = new[] { "내 솜씨다: {list}. 나머지는 아직 10." },
                ["skillsNone"] = new[] { "아직 모두 10이다. 곧 달라질 것이다." },
                ["craft.plan"] = new[] { "{item} {n}. {short|이} 없으니 {minutes}분은 걸린다. 바로 시작한다." },
                ["craft.inStock"] = new[] { "{item} {n|이라}? 이미 상자에 있다." },
                ["craft.missing"] = new[] { "{item|을} 만들려면 {station|이} 필요하다. 지어 둬라." },
                ["craft.tooLong"] = new[] { "{minutes}분이 넘게 걸리는 일이다. 나눠서 시켜라." },
                ["craft.unknown"] = new[] { "{what|은} 어디서 나는지 아직 모른다." },
                ["setList"] = new[] { "세트메뉴다: {list}." },
                ["setEmpty"] = new[] { "저장된 세트는 없다." },
                ["setNone"] = new[] { "{no}번 세트 따위는 없다." },
                ["setSaved"] = new[] { "{no}번 세트, 기억했다." },
                ["keepSet"] = new[] { "{item}, {n}개 아래로 떨어지면 채워 주지." },
                ["keepOff"] = new[] { "{item}, 더는 채우지 않겠다." },
                ["farmSet"] = new[] { "반경 {r}m, 밭으로 기억했다." },
                ["farmMoved"] = new[] { "밭을 이리 옮겼다. 반경 {r}m." },
                ["farmFull"] = new[] { "밭이 여덟이다. 더는 안 된다." },
                ["farmNoGrow"] = new[] { "이 땅엔 아무것도 안 자란다. 다른 곳을 골라라." },
                ["farmWhere"] = new[] { "어디를 말하는 거지? 그 자리에 서서 다시 말해라." },
                ["farmNoTool"] = new[] { "경작기가 없다. 장비 상자에 하나 넣어라." },
                ["farmNoSeed"] = new[] { "심을 씨앗이 없다. 상자에 넣어 둬라." },
                ["farmGo"] = new[] { "밭에 {list}, 심고 오지." },
                ["farmDone"] = new[] { "{n}개 심었다.{more}" },
                ["farmNoRoom"] = new[] { " 빈자리가 없어 {left}개는 못 심었다." },
                ["farmHarvest"] = new[] { "{list}, 거둬 왔다." },
                ["farmPlantFor"] = new[] { "{item}? 없다. 밭에 심어 두지. {minutes}분쯤이다." },
                ["craft.farm"] = new[] { "{item|을} 만들려면 {what|이} 필요하다. 밭에 심어 두지. {minutes}분 뒤에 다시 시켜라." },
                ["farmHarvestFor"] = new[] { "밭에 {item} 다 자랐다. 거둬 오지." },
                ["farmNoField"] = new[] { "{item}, 밭에서 나는 거다. 심을 밭부터 정해라. 그 자리에서 !밭 10." },
                ["farmNoSeedFor"] = new[] { "{item} 씨앗이 없다. 상자에 넣어 둬라." },
                ["state.farm"] = new[] { "밭일 중({n}/{total})" },
                ["job.farm"] = new[] { "밭일" },
                ["farmAlready"] = new[] { "그 밭은 {npc}가 맡았다." },
                ["refillGo"] = new[] { "{item|이} 모자라는군. {n}개 가져오지." },
                ["refillMake"] = new[] { "{item|이} 모자라는군. {n}개 만들어 두지. {minutes}분." },
                ["depart.warn"] = new[] { "{item} {n}. 그곳엔 {foe|이} 있지만 상관없다." },
                ["depart.hungry"] = new[] { "굶긴 채로 보내는군. 기억해 두지." },
                ["depart.far"] = new[] { "{minutes}분은 걸린다. 포탈 하나 없이 나를 이리 부리다니." },
                ["depart.trips"] = new[] { "한 번에 다 들 수는 없다. {trips}번 오가지." },
                ["depart.lowSkill"] = new[] { "이 일엔 아직 덜 익었다. 시간이 좀 걸린다." },
                ["depart.busy"] = new[] { "{job}부터 끝낸다. 기다려라." },
                ["depart.together"] = new[] { "{partner}, 따라와라. 네 쓸모를 보여 줄 기회다." },
                ["depart.outOfScope"] = new[] { "그건 내 힘으로 구할 수 있는 것이 아니다." },
                ["depart.noCrypt"] = new[] { "철은 늪 지하묘지에 있다. 문이 닫혀 있어 못 간다. 열어라." },
                ["depart.cryptsEmpty"] = new[] { "아는 묘지는 이미 다 파냈다. 새 묘지를 찾아와라." },
                ["cryptOpen"] = new[] { "열쇠의 힘은 이미 네가 얻었다. 문은 내가 연다." },
                ["ask.alternative"] = new[] { "{foe} 무리와 혼자 싸우는 건 어리석다. {altDir}쪽 {altDist}m가 낫겠지. 그리로 갈까?{extra}" },
                ["ask.escort"] = new[] { "{foe}... 혼자 상대할 가치는 없다. 따라오겠나?{extra}" },
                ["ask.diedThere"] = new[] { "그곳에서 한 번 쓰러졌다. 이번엔 네가 같이 가라.{extra}" },
                ["ask.condition"] = new[] { "체력 미드 하나를 내놓아라. 그러면 간다.{extra}" },
                ["depart.noResist"] = new[] { "{mead}도 없이 보내다니. 좋다, {item} {n} 가져오지." },
                ["ask.night"] = new[] { "곧 밤이다. 아침에 가겠나?{extra}" },
                ["ask.stock"] = new[] { "상자에 {item} {stock}개가 있다. 그래도 더 원하나?" },
                ["note.stock"] = new[] { " 상자에 {stock}개 있는 건 알고 있겠지." },
                ["note.night"] = new[] { " 밤도 다가온다." },
                ["answer.grumble"] = new[] { "좋다. 후회는 네 몫이다." },
                ["answer.escort"] = new[] { "따라와라. 뒤처지지 말고." },
                ["answer.alternative"] = new[] { "{altDir}쪽이다. 현명한 선택이군." },
                ["answer.conditionMet"] = new[] { "미드는 받았다. 간다." },
                ["answer.conditionMissing"] = new[] { "미드가 없다. 상자에 넣어라." },
                ["answer.morning"] = new[] { "아침에 간다." },
                ["answer.go"] = new[] { "간다." },
                ["answer.hold"] = new[] { "그럼 접어 두지." },
                ["answer.cancel"] = new[] { "그만두지." },
                ["answer.timeout"] = new[] { "대답이 없군. 접어 두겠다." },
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
