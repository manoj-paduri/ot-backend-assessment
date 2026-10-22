namespace OT.Assessment.App.Contracts;

/// <summary>Body of the 202 response: the wager is safely on the bus and will be stored asynchronously.</summary>
public sealed record WagerAcceptedResponse(Guid WagerId);
