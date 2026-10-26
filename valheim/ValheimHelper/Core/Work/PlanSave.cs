using System;
using System.Collections.Generic;
using System.Globalization;

namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 제작 계획 저장(외부 검토 + 사용자 승인 10-26): 재시작 뒤 계획을 현재 재고로 다시 세우지 않고, 저장한 단계·상태·진행으로 이어 간다.
    /// 다시 세우면 설비에 넣은 재료·나오는 중인 결과물을 몰라 재료를 또 모으고 더 만들었다(설비 속 재료를 재고로 세면 광석을 다시 꺼낸다거나
    /// 아직 안 나온 주괴로 제작을 시작하는 오판, 두 작업이 같은 설비를 세면 중복). 이어 가면 "기다렸다 회수할 물량"은 그 작업의
    /// 남은 기다림·꺼내기 단계로 남고, 다른 작업과 나눠 세지 않는다.
    /// 형식: "v1" ~ 단계마다 "id/kind/item/amount/station/fuel/fuelAmount/crafts/attend/seconds/after(+)/state/progress".
    /// 물건·설비 이름은 프리팹·제작대 이름이라 / ~ + 를 쓰지 않는다(쓰면 그 칸은 깨진 것으로 봄). 바깥 저장(WorkState)이 % ; , = | 를 감싼다.
    /// </summary>
    public static class PlanSave
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string Version = "v1";

        /// <param name="progress">단계 번호 → 진행(채집·사냥: 지금까지 상자에 넣은 수), 모르면 0.</param>
        public static string Save(PlanRunner runner, Func<int, int> progress)
        {
            var parts = new List<string> { Version };
            foreach (var s in runner.Plan.Steps)
            {
                var after = new List<string>();
                foreach (int a in s.After) after.Add(a.ToString(Inv));
                parts.Add(string.Join("/", new[]
                {
                    s.Id.ToString(Inv), ((int)s.Kind).ToString(Inv), s.Item ?? "", s.Amount.ToString(Inv), s.Station ?? "", s.Fuel ?? "",
                    s.FuelAmount.ToString(Inv), s.Crafts.ToString(Inv), s.AttendSeconds.ToString("R", Inv), s.Seconds.ToString("R", Inv),
                    string.Join("+", after.ToArray()), ((int)runner.StateOf(s.Id)).ToString(Inv), Math.Max(0, progress?.Invoke(s.Id) ?? 0).ToString(Inv),
                }));
            }
            return string.Join("~", parts.ToArray());
        }

        /// <summary>
        /// 저장한 계획을 되살린다(PlanRunner.Restore 규칙으로 진행 중이던 단계를 맞춤). 깨졌거나 다른 형식이면 null(부르는 쪽이 다시 계획).
        /// </summary>
        /// <param name="alreadyLoaded">곁에서 지키는 설비(조리대)의 넣기가 진행 중이었을 때 이미 올라가 있는지(플러그인이 설비를 봄). null이면 아니라고 봄.</param>
        public static PlanRunner Load(string text, Func<PlanStep, bool> alreadyLoaded = null)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] parts = text.Split('~');
            if (parts.Length < 2 || parts[0] != Version) return null;
            var plan = new Plan();
            var states = new StepState[parts.Length - 1];
            var progress = new int[parts.Length - 1];
            for (int i = 1; i < parts.Length; i++)
            {
                string[] f = parts[i].Split('/');
                if (f.Length != 13) return null;
                if (!Int(f[0], out int id) || id != i - 1) return null;
                if (!Int(f[1], out int kind) || !Enum.IsDefined(typeof(StepKind), kind)) return null;
                if (!Int(f[11], out int state) || !Enum.IsDefined(typeof(StepState), state)) return null;
                var s = new PlanStep { Id = id, Kind = (StepKind)kind, Item = Str(f[2]), Station = Str(f[4]), Fuel = Str(f[5]) };
                if (!Int(f[3], out s.Amount) || !Int(f[6], out s.FuelAmount) || !Int(f[7], out s.Crafts) || !Int(f[12], out progress[id])) return null;
                if (!float.TryParse(f[8], NumberStyles.Float, Inv, out s.AttendSeconds) || !float.TryParse(f[9], NumberStyles.Float, Inv, out s.Seconds)) return null;
                if (f[10].Length > 0)
                    foreach (string a in f[10].Split('+'))
                    {
                        if (!Int(a, out int d) || d < 0 || d >= id) return null; // 앞 단계만 기댈 수 있다(계획이 그렇게 만든다)
                        s.After.Add(d);
                    }
                states[id] = (StepState)state;
                plan.Steps.Add(s);
            }
            return PlanRunner.Restore(plan, states, progress, alreadyLoaded);
        }

        static string Str(string s) => s.Length == 0 ? null : s;
        static bool Int(string s, out int v) => int.TryParse(s, NumberStyles.Integer, Inv, out v);
    }
}
