namespace ResourceProvisioner.Api.Tests;

using Models;
using Services.Requests;
using Xunit;

public class RequestStateMachineTests
{
  private static readonly (RequestStatus From, RequestStatus To)[] Legal =
  [
    (RequestStatus.AwaitingConfirmation, RequestStatus.Queued),
    (RequestStatus.Queued, RequestStatus.Applying),
    (RequestStatus.Queued, RequestStatus.Failed),
    (RequestStatus.Applying, RequestStatus.Succeeded),
    (RequestStatus.Applying, RequestStatus.Failed),
    (RequestStatus.Succeeded, RequestStatus.Destroying),
    (RequestStatus.Failed, RequestStatus.Destroying),
    (RequestStatus.Destroying, RequestStatus.DestroyFailed),
    (RequestStatus.DestroyFailed, RequestStatus.Destroying),
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

  [Fact]
  public void NextStatuses_HasNoFinalStatus_SinceFinishedRequestsAreDeleted()
  {
    Assert.All(Enum.GetValues<RequestStatus>(), s => Assert.NotEmpty(RequestStateMachine.NextStatuses(s)));
    Assert.Equal(
      [
        RequestStatus.AwaitingConfirmation, RequestStatus.Queued, RequestStatus.Applying, RequestStatus.Succeeded,
        RequestStatus.Failed, RequestStatus.Destroying, RequestStatus.DestroyFailed,
      ],
      Enum.GetValues<RequestStatus>());
  }

  [Fact]
  public void ActiveStatuses_AreTheConfirmedUnfinishedOnes()
  {
    Assert.Equal(
      [RequestStatus.Queued, RequestStatus.Applying, RequestStatus.Succeeded, RequestStatus.Destroying],
      RequestStateMachine.ActiveStatuses);
  }

  [Theory]
  [InlineData(RequestStatus.Succeeded)]
  [InlineData(RequestStatus.Failed)]
  [InlineData(RequestStatus.DestroyFailed)]
  public void Move_RecordsTheStatusBeforeDestroy_AndClearsWhatStoppedTheLastOne(RequestStatus from)
  {
    var request = NewRequest(from);
    request.DestroyStoppedById = Guid.NewGuid();

    RequestStateMachine.Move(request, RequestStatus.Destroying, DateTimeOffset.UtcNow);

    Assert.Equal((RequestStatus.Destroying, from, (Guid?)null),
      (request.Status, request.StatusBeforeDestroy, request.DestroyStoppedById));
  }

  [Fact]
  public void Move_ForgetsTheStatusBeforeDestroy_WhenTheDestroyFails()
  {
    var request = NewRequest(RequestStatus.Succeeded);
    RequestStateMachine.Move(request, RequestStatus.Destroying, DateTimeOffset.UtcNow);

    RequestStateMachine.Move(request, RequestStatus.DestroyFailed, DateTimeOffset.UtcNow);

    Assert.Null(request.StatusBeforeDestroy);
  }

  [Theory]
  [InlineData(RequestStatus.Succeeded)]
  [InlineData(RequestStatus.Failed)]
  [InlineData(RequestStatus.DestroyFailed)]
  public void RevertDestroy_GoesBackToTheStatusBeforeDestroy_AndRecordsWhatStoppedIt(RequestStatus before)
  {
    var request = NewRequest(before);
    RequestStateMachine.Move(request, RequestStatus.Destroying, DateTimeOffset.UtcNow);
    var stoppedBy = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow.AddMinutes(5);

    RequestStateMachine.RevertDestroy(request, stoppedBy, now);

    Assert.Equal((before, (RequestStatus?)null, (Guid?)stoppedBy, now),
      (request.Status, request.StatusBeforeDestroy, request.DestroyStoppedById, request.UpdatedAt));
  }

  [Fact]
  public void RevertDestroy_GoesToFailed_WhenTheStatusBeforeDestroyWasntRecorded()
  {
    var request = NewRequest(RequestStatus.Destroying);

    RequestStateMachine.RevertDestroy(request, Guid.NewGuid(), DateTimeOffset.UtcNow);

    Assert.Equal(RequestStatus.Failed, request.Status);
  }

  [Theory]
  [InlineData(RequestStatus.Succeeded)]
  [InlineData(RequestStatus.DestroyFailed)]
  public void RevertDestroy_Throws_WhenTheRequestIsntDestroying(RequestStatus status)
  {
    var request = NewRequest(status);

    Assert.Throws<RequestConflictException>(
      () => RequestStateMachine.RevertDestroy(request, Guid.NewGuid(), DateTimeOffset.UtcNow));
    Assert.Equal(status, request.Status);
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
