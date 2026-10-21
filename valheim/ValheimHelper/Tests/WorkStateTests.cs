using System.Collections.Generic;
using ValheimHelper.Core.Npc;
using ValheimHelper.Core.Work;
using Xunit;

public class WorkStateTests
{
    [Fact]
    public void QueueRoundTrip()
    {
        var items = new List<WorkItem>
        {
            new WorkItem { Kind = WorkKind.Gather, Resume = true, Label = "나무 50", Item = "Wood", Amount = 37, HasZone = true, Zone = new ZoneXY(-3, 12) },
            new WorkItem { Kind = WorkKind.Gather, Label = "구리 광석 20", Item = "CopperOre", Amount = 20, Speaker = "lee;=,|%", Target = "Alarak", Urgent = true, Ready = ReadyWhen.Morning, NotBefore = 3780.5 },
            new WorkItem { Kind = WorkKind.Explore, Label = "동쪽", Arg = "east" },
            new WorkItem { Kind = WorkKind.Explore, Resume = true, Label = "늪 탐험 중", DirX = 1, DirY = -1, BiomeId = 2, Where = "늪", Biome = "Swamp" },
        };
        string text = WorkState.SaveQueue(items);
        var back = WorkState.LoadQueue(text);
        Assert.Equal(4, back.Count);
        Assert.True(back[0].Resume);
        Assert.Equal("Wood", back[0].Item);
        Assert.Equal(37, back[0].Amount);
        Assert.True(back[0].HasZone);
        Assert.Equal(new ZoneXY(-3, 12), back[0].Zone);
        Assert.Equal(ReadyWhen.Always, back[0].Ready);
        Assert.Equal("lee;=,|%", back[1].Speaker);
        Assert.Equal("Alarak", back[1].Target);
        Assert.True(back[1].Urgent);
        Assert.False(back[1].Resume);
        Assert.False(back[1].HasZone);
        Assert.Equal(ReadyWhen.Morning, back[1].Ready);
        Assert.Equal(3780.5, back[1].NotBefore);
        Assert.Equal("구리 광석 20", back[1].Label);
        Assert.Equal(WorkKind.Explore, back[2].Kind);
        Assert.Equal("east", back[2].Arg);
        Assert.Null(back[2].Biome);
        Assert.Equal(1, back[3].DirX);
        Assert.Equal(-1, back[3].DirY);
        Assert.Equal(2, back[3].BiomeId);
        Assert.Equal("늪", back[3].Where);
        Assert.Equal("Swamp", back[3].Biome);
        Assert.Equal(text, WorkState.SaveQueue(back)); // 다시 저장해도 같음(ZDO에 다시 안 씀)
    }

    [Fact]
    public void QueueEmptyAndBroken()
    {
        Assert.Equal("", WorkState.SaveQueue(new List<WorkItem>()));
        Assert.Empty(WorkState.LoadQueue(""));
        Assert.Empty(WorkState.LoadQueue(null));
        // 종류 모름·물건 없는 채집·수량 0은 버리고 나머지는 읽는다
        var back = WorkState.LoadQueue("k=9,l=x;k=0,l=y;k=0,i=Wood,n=0;garbage;k=0,i=Stone,n=5,zz=1,z=bad");
        Assert.Single(back);
        Assert.Equal("Stone", back[0].Item);
        Assert.False(back[0].HasZone);
    }

    [Fact]
    public void PendingRoundTrip()
    {
        var p = new PendingData
        {
            Question = Question.Alternative, Condition = "healthMead", HasPlace = true, Place = new ZoneXY(4, -7),
            HasAlternative = true, Alternative = new ZoneXY(-1, 2), AskedAt = 12345.678, Speaker = "친구",
            Item = "CopperOre", Amount = 30, Urgent = true, Target = "Claude",
        };
        p.Args["item"] = "구리 광석";
        p.Args["foe"] = "Troll";
        p.Args["n"] = "30";
        p.Args["odd"] = "a=b,c;d";
        string text = WorkState.SavePending(p);
        var back = WorkState.LoadPending(text);
        Assert.NotNull(back);
        Assert.Equal(Question.Alternative, back.Question);
        Assert.Equal("healthMead", back.Condition);
        Assert.Equal(new ZoneXY(4, -7), back.Place);
        Assert.Equal(new ZoneXY(-1, 2), back.Alternative);
        Assert.True(back.HasPlace && back.HasAlternative);
        Assert.Equal(12345.678, back.AskedAt);
        Assert.Equal("친구", back.Speaker);
        Assert.Equal("CopperOre", back.Item);
        Assert.Equal(30, back.Amount);
        Assert.True(back.Urgent);
        Assert.Equal("Claude", back.Target);
        Assert.Equal(4, back.Args.Count);
        Assert.Equal("a=b,c;d", back.Args["odd"]);
        Assert.Equal("구리 광석", back.Args["item"]);
        Assert.Equal(text, WorkState.SavePending(back));
    }

    [Fact]
    public void PendingNoneAndBroken()
    {
        Assert.Equal("", WorkState.SavePending(null));
        Assert.Null(WorkState.LoadPending(""));
        Assert.Null(WorkState.LoadPending("q=99,at=1,i=Wood,n=1"));
        Assert.Null(WorkState.LoadPending("q=1,i=Wood,n=1")); // 시각 없음
        Assert.Null(WorkState.LoadPending("q=1,at=5")); // 명령 없음
        var p = WorkState.LoadPending("q=6,at=5,i=Wood,n=10");
        Assert.Equal(Question.Stock, p.Question);
        Assert.False(p.HasPlace);
        Assert.Empty(p.Args);
    }

    [Fact]
    public void ExpiredAtTimeout()
    {
        var p = new PendingData { AskedAt = 100 };
        Assert.False(WorkState.Expired(p, 100 + Replies.Timeout));
        Assert.True(WorkState.Expired(p, 100 + Replies.Timeout + 0.01));
        Assert.False(WorkState.Expired(null, 1e9));
    }

    [Fact]
    public void MorningWaitsForNextMorning()
    {
        const double len = 1200;
        // 하루 0.74(곧 밤)에 "응" → 다음 날 0.15
        Assert.Equal(len + 180, WorkState.NextMorning(0.74 * len, len), 6);
        // 자정 넘은 새벽(다음 날 0.05)에 물으면 그날 아침
        Assert.Equal(len + 180, WorkState.NextMorning(len + 0.05 * len, len), 6);
        // 아침 시작 바로 뒤면 다음 날
        Assert.Equal(2 * len + 180, WorkState.NextMorning(len + 0.2 * len, len), 6);
        var w = new WorkItem { Ready = ReadyWhen.Morning, NotBefore = len + 180 };
        Assert.False(WorkState.IsReady(w, 0.74 * len, false)); // 아직 낮이어도 아침 전이면 안 꺼냄(질문 되풀이 방지)
        Assert.False(WorkState.IsReady(w, len + 100, true));
        Assert.False(WorkState.IsReady(w, len + 200, true)); // 시각은 지났어도 아직 어두우면
        Assert.True(WorkState.IsReady(w, len + 300, false));
        Assert.True(WorkState.IsReady(new WorkItem(), 0, true)); // 조건 없음
    }

    [Fact]
    public void EscapeRoundTrip()
    {
        foreach (var s in new[] { "", "plain", "%", "%%41", "a;b,c=d|e%f", "한글;쉼표," })
            Assert.Equal(s, WorkState.Unesc(WorkState.Esc(s)));
        Assert.Equal("100%", WorkState.Unesc("100%")); // 끝의 % 혼자는 그대로
    }
}
