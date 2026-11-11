namespace ValheimHelper.Core.Work
{
    /// <summary>
    /// 일꾼 ZDO 여럿에 같은 공유 값(세트·보충·밭·개인·빚)이 있을 때 고르기(사용자 11-11 작은 것 ①).
    /// 저장마다 올리는 번호가 큰 쪽(옛 저장은 번호 없음 = 0), 같으면 글자가 긴 쪽 — 한 ZDO만 저장이 건너뛰어진 채 꺼져도 지운 항목이 되살아나지 않게.
    /// </summary>
    public static class SaveStamp
    {
        /// <summary>(번호, 글자)가 지금 고른 (bestNo, best)보다 나으면 true.</summary>
        public static bool Better(long no, string text, long bestNo, string best)
        {
            if (no != bestNo) return no > bestNo;
            return (text ?? "").Length > (best ?? "").Length;
        }
    }
}
