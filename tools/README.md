# 개발 도구

1.7 게임 API(참조 어셈블리)를 추측 없이 확인하는 도구. 참조 어셈블리는 NuGet 캐시에 있다
(`~/.nuget/packages/krafs.rimworld.ref/<버전>/ref/net10.0/Assembly-CSharp.dll`, 모드를 한 번 빌드하면 생김).

```bash
# 타입·필드·메서드 이름 전체 덤프
dotnet run --project tools/apidump -c Release -- <dll> > /tmp/api.txt
# 특정 타입 필드의 특성(번역 이름표 규칙 등)
dotnet run --project tools/attrdump -c Release -- <dll> Verse.BodyPartRecord Verse.Tool
```

바닐라 XML Def 조회는 `scripts/vanilla-defs.py`.
