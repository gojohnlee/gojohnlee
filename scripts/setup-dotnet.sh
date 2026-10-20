#!/usr/bin/env bash
# 클라우드 세션용: .NET 10 SDK 설치 (dot.net은 네트워크 정책상 막혀 있어 apt 사용)
set -euo pipefail
if ! dotnet --list-sdks 2>/dev/null | grep -q "^10\."; then
  apt-get update -qq
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-10.0
fi
dotnet --version
