using ShieldGuard;
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
bool Cross(float ax,float az,float bx,float bz) => ShieldMath.Crosses(ax,az,bx,bz,0,0,0,1,1.25f,out _);
Check(Cross(0,5,0,-5), "front shot protecting rear ally");
Check(!Cross(0,-5,0,5), "rear shot passes");
Check(!Cross(2,5,2,-5), "outside width passes");
Check(Cross(1.25f,5,1.25f,-5), "shield edge blocks");
Check(!Cross(-2,0,2,0), "parallel shot passes");
Check(!Cross(0,5,0,2), "shot has not reached shield");
Check(Cross(0,1,0,0), "final flight tick hits shield");
Check(ShieldMath.Crosses(5,0,-5,0,0,0,1,0,1.25f,out var t) && Math.Abs(t-.5f)<.001f,"east facing");
Check(ShieldMath.Crosses(0,5,0,-5,0,2,0,1,1.25f,out var near) &&
      ShieldMath.Crosses(0,5,0,-5,0,-2,0,1,1.25f,out var far) && near < far,"nearest shield wins");
float energy=120;
Check(ShieldMath.Absorb(ref energy,30)==0 && energy==90,"full absorption");
Check(ShieldMath.Absorb(ref energy,120)==30 && energy==0,"overflow goes to body");
Check(ShieldMath.Absorb(ref energy,20)==20 && energy==0,"depleted shield");
energy=5;
Check(ShieldMath.Absorb(ref energy,5)==0 && energy==0,"exact depletion");
Console.WriteLine($"PASS: {checks} shield geometry and damage checks.");
