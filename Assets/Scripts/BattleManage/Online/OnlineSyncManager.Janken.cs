using System.Threading.Tasks;
using Photon.Pun;

public partial class OnlineSyncManager
{
    int jankenRound = -1;
    int pendingJankenHand = -1;
    int pendingJankenRound = -1;
    TaskCompletionSource<int> jankenHandTcs;

    bool jankenChoiceReady;
    bool jankenHostGoesFirst;
    TaskCompletionSource<bool> jankenChoiceTcs;

    int localJankenReadyId;
    int remoteJankenReadyId;
    TaskCompletionSource<bool> jankenReadyTcs;

    public void ResetJankenSession()
    {
        jankenRound = -1;
        pendingJankenHand = -1;
        pendingJankenRound = -1;
        jankenHandTcs = null;
        jankenChoiceReady = false;
        jankenHostGoesFirst = true;
        jankenChoiceTcs = null;
    }

    /// <summary>
    /// 両者のデッキ読み込みが終わってから手を送り始めるための待ち合わせ。
    /// </summary>
    public async Task WaitUntilPeerReadyForJanken()
    {
        localJankenReadyId++;
        jankenReadyTcs = new TaskCompletionSource<bool>();
        if (remoteJankenReadyId >= localJankenReadyId)
            jankenReadyTcs.TrySetResult(true);

        do
        {
            SendRPC("ReceiveJankenReady", localJankenReadyId);
            if (jankenReadyTcs.Task.IsCompleted)
                break;
            await Task.WhenAny(jankenReadyTcs.Task, Task.Delay(200));
        }
        while (!jankenReadyTcs.Task.IsCompleted);
    }

    [PunRPC]
    private void ReceiveJankenReady(int id)
    {
        if (id > remoteJankenReadyId)
            remoteJankenReadyId = id;
        if (jankenReadyTcs != null && localJankenReadyId > 0 && remoteJankenReadyId >= localJankenReadyId)
            jankenReadyTcs.TrySetResult(true);
    }

    public void PrepareJankenRound(int round)
    {
        jankenRound = round;
        jankenHandTcs = new TaskCompletionSource<int>();
        if (pendingJankenRound == round && pendingJankenHand >= 0)
            jankenHandTcs.TrySetResult(pendingJankenHand);
    }

    public void SubmitJankenHand(int hand, int round)
    {
        SendRPC("ReceiveJankenHand", hand, round);
    }

    public Task<int> WaitForOpponentJankenHand()
    {
        return jankenHandTcs.Task;
    }

    [PunRPC]
    private void ReceiveJankenHand(int hand, int round)
    {
        pendingJankenHand = hand;
        pendingJankenRound = round;
        if (round == jankenRound)
            jankenHandTcs?.TrySetResult(hand);
    }

    public void SubmitJankenFirstChoice(bool hostGoesFirst)
    {
        SendRPC("ReceiveJankenFirstChoice", hostGoesFirst);
    }

    public Task<bool> WaitForJankenFirstChoice()
    {
        jankenChoiceTcs = new TaskCompletionSource<bool>();
        if (jankenChoiceReady)
            jankenChoiceTcs.TrySetResult(jankenHostGoesFirst);
        return jankenChoiceTcs.Task;
    }

    [PunRPC]
    private void ReceiveJankenFirstChoice(bool hostGoesFirst)
    {
        jankenHostGoesFirst = hostGoesFirst;
        jankenChoiceReady = true;
        jankenChoiceTcs?.TrySetResult(hostGoesFirst);
    }
}
