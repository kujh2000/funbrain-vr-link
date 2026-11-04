using System;
using System.Collections.Generic;

namespace FunBrainLab.VRLink
{
    /// <summary>
    /// 웹앱과 주고받는 통로. USB(ADB 파일)와 네트워크(Firebase RTDB)를 동시에 쓸 수 있습니다.
    /// </summary>
    public interface IControlChannel
    {
        string Name { get; }
        /// <summary>웹앱에서 파라미터 값이 왔을 때 {id: 값}</summary>
        event Action<Dictionary<string, object>> ParamsReceived;
        /// <summary>웹앱에서 동작 명령이 왔을 때 {actionId: 요청번호}</summary>
        event Action<Dictionary<string, object>> ActionsReceived;
        void Open();
        /// <summary>hello / schema / values / monitors / status 같은 최신 상태 덮어쓰기</summary>
        void PutState(string key, string json);
        /// <summary>상태 일부만 갱신 (monitors 처럼 자주 바뀌는 값)</summary>
        void PatchState(string key, string json);
        /// <summary>수행 기록 한 줄 추가</summary>
        void AppendLog(string json);
        /// <summary>매 프레임 호출 (폴링이 필요한 채널용)</summary>
        void Tick();
        void Close();
    }
}
