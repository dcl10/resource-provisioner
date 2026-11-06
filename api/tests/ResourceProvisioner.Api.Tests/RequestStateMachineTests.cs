namespace ResourceProvisioner.Api.Tests;

using Models;
using Services.Requests;
using Xunit;

public class RequestStateMachineTests
{
  private static readonly (RequestStatus From, RequestStatus To)[] Legal =
  [
    (RequestStatus.AwaitingConfirmation, RequestStatus.Queued),
    (RequestStatus.AwaitingConfirmation, RequestStatus.Cancelled),
    (RequestStatus.Queued, RequestStatus.Applying),
    (RequestStatus.Applying, RequestStatus.Succeeded),
    (RequestStatus.Applying, RequestStatus.Failed),
    (RequestStatus.Succeeded, RequestStatus.Destroying),
    (RequestStatus.Failed, RequestStatus.Destroying),
    (RequestStatus.Destroying, RequestStatus.Destroyed),
    (RequestStatus.Destroying, RequestStatus.Failed),
  ];

  public static TheoryData<RequestStatus, RequestStatus> LegalMoves()
  {
    var data = new TheoryData<RequestStatus, RequestStatus>();
    foreach (var (from, to) in Legal)
    {
      data.Add(from, to);
    }

    return data;
  }

  public static TheoryData<RequestStatus, RequestStatus> IllegalMoves()
  {
    var data = new TheoryData<RequestStatus, RequestStatus>();
    foreach (var from in Enum.GetValues<RequestStatus>())
    {
      foreach (var to in Enum.GetValues<RequestStatus>().Where(to => !Legal.Contains((from, to))))
      {
        data.Add(from, to);
      }
    }

    return data;
  }

  [Theory]
  [MemberData(nameof(LegalMoves))]
  public void Move_ChangesStatusAndUpdatedAt_WhenMoveIsLegal(RequestStatus from, RequestStatus to)
  {
    var request = NewRequest(from);
    var now = DateTimeOffset.UtcNow.AddMinutes(5);

    RequestStateMachine.Move(request, to, now);

    Assert.Equal(to, request.Status);
    Assert.Equal(now, request.UpdatedAt);
  }

  [Theory]
  [MemberData(nameof(IllegalMoves))]
  public void Move_ThrowsAndLeavesRequestUnchanged_WhenMoveIsIllegal(RequestStatus from, RequestStatus to)
  {
    var request = NewRequest(from);
    var updatedAt = request.UpdatedAt;

    Assert.False(RequestStateMachine.CanMove(from, to));
    Assert.Throws<RequestConflictException>(() => RequestStateMachine.Move(request, to, DateTimeOffset.UtcNow));
    Assert.Equal(from, request.Status);
    Assert.Equal(updatedAt, request.UpdatedAt);
  }

  [Fact]
  public void Move_SetsConfirmedAt_WhenQueued()
  {
    var request = NewRequest(RequestStatus.AwaitingConfirmation);
    var now = DateTimeOffset.UtcNow;

    RequestStateMachine.Move(request, RequestStatus.Queued, now);

    Assert.Equal(now, request.ConfirmedAt);
  }

  [Theory]
  [InlineData(RequestStatus.Destroyed)]
  [InlineData(RequestStatus.Cancelled)]
  public void NextStatuses_IsEmpty_ForFinalStatus(RequestStatus status)
  {
    Assert.Empty(RequestStateMachine.NextStatuses(status));
  }

  [Fact]
  public void ActiveStatuses_AreTheConfirmedUnfinishedOnes()
  {
    Assert.Equal(
      [RequestStatus.Queued, RequestStatus.Applying, RequestStatus.Succeeded, RequestStatus.Destroying],
      RequestStateMachine.ActiveStatuses);
  }

  private static ProvisioningRequest NewRequest(RequestStatus status) =>
    new()
    {
      Id = Guid.NewGuid(),
      OwnerId = "user-1",
      Type = "test-server",
      Variables = "{}",
      Status = status,
      UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}
