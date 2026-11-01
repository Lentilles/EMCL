using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Registers;

namespace EMCL.Tests.Objects.Registers;

public sealed class RegisterActivationTests
{
    [Fact]
    public async Task AddsAssignmentsReplacesMasksAndSelectsUsingOneBasedIndices()
    {
        var device = new EmclBuilder().Build("meter");
        var activation = device.Add(6, 0, LogicalName.Parse("0.0.14.0.0.255"));
        var first = device.Add(4, 0, LogicalName.Parse("1.0.1.8.0.255"));
        var second = device.Add(5, 0, LogicalName.Parse("1.0.1.6.0.255"));
        var unassigned = device.Add(3, 0, LogicalName.Parse("1.0.2.8.0.255"));
        var handler = Assert.IsType<RegisterActivationHandler>(device.Handlers.Resolve("meter", activation));
        Assert.Empty((await ExecuteOperationAsync(device, activation, CosemOperationKind.Read, 2)).Value!.Items);
        Assert.Equal(CosemError.Success, (await Invoke(1, Reference(first))).Error);
        Assert.Equal(CosemError.Success, (await Invoke(1, Reference(second))).Error);
        Assert.False(await Enabled(first));
        Assert.True(await Enabled(unassigned));
        var name = DlmsValue.Octets([0, 255, 128]);
        Assert.Equal(CosemError.Success, (await Invoke(2, Mask(name, 1))).Error);
        await WriteValueAsync(device, activation, 4, name);
        Assert.True(await Enabled(first));
        Assert.False(await Enabled(second));
        Assert.Equal(CosemError.Success, (await Invoke(2, Mask(name, 2))).Error);
        Assert.Single(ReadStoredValue(activation, 3).Items);
        Assert.False(await Enabled(first));
        Assert.True(await Enabled(second));
        Assert.Equal(CosemError.TemporaryFailure, (await Invoke(3, name)).Error);
        var emptyName = DlmsValue.Octets([]);
        Assert.Equal(CosemError.Success, (await Invoke(2, Mask(emptyName, 1, 2))).Error);
        await WriteValueAsync(device, activation, 4, emptyName);
        Assert.Equal(CosemError.Success, (await Invoke(3, name)).Error);
        Assert.Equal(CosemError.ObjectUndefined, (await Invoke(3, name)).Error);
        Assert.True(await Enabled(first));
        Assert.True(await Enabled(second));

        ValueTask<OperationResult> Invoke(sbyte method, DlmsValue value) => ExecuteOperationAsync(device, activation,
            CosemOperationKind.Invoke, method, value);
        ValueTask<bool> Enabled(CosemObject register) => handler.IsEnabledAsync(activation, register.LogicalName,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RejectsBadReferencesIndicesAndCrossAttributeChangesWithoutMutation()
    {
        var device = new EmclBuilder().Build("meter");
        var activation = device.Add(6, 0, LogicalName.Parse("0.0.14.0.0.255"));
        var register = device.Add(3, 0, LogicalName.Parse("1.0.1.8.0.255"));
        var data = device.Add(1, 0, LogicalName.Parse("0.0.96.1.0.255"));
        Assert.Equal(CosemError.ObjectClassInconsistent, (await Invoke(1, Reference(data))).Error);
        var absent = DlmsValue.Structure(Number(DlmsType.UInt16, 3), DlmsValue.Octets(LogicalName.Parse("1.0.9.8.0.255").ToBytes()));
        Assert.Equal(CosemError.ObjectUndefined, (await Invoke(1, absent)).Error);
        Assert.False(activation.State.TryGet(2, out _));
        Assert.Equal(CosemError.Success, (await Invoke(1, Reference(register))).Error);
        var assignments = ReadStoredValue(activation, 2);
        var name = DlmsValue.Octets([1]);
        foreach (byte index in new byte[] { 0, 2, 255 })
        {
            Assert.Equal(CosemError.ScopeOfAccessViolated, (await Invoke(2, Mask(name, index))).Error);
        }

        Assert.Equal(CosemError.TypeUnmatched, (await Invoke(2, DlmsValue.Structure(name, DlmsValue.Array(Number(DlmsType.UInt16, 1))))).Error);
        Assert.False(activation.State.TryGet(3, out _));
        Assert.Equal(CosemError.Success, (await Invoke(2, Mask(name, 1))).Error);
        var masks = ReadStoredValue(activation, 3);
        await WriteValueAsync(device, activation, 4, name);
        Assert.Equal(CosemError.ScopeOfAccessViolated, (await Write(2, DlmsValue.Array())).Error);
        Assert.Equal(CosemError.ObjectUndefined, (await Write(3, DlmsValue.Array())).Error);
        Assert.Equal(CosemError.TypeUnmatched, (await Write(3, DlmsValue.Array(Mask(name, 1), Mask(name, 1)))).Error);
        Assert.Equal(CosemError.ObjectUndefined, (await Write(4, DlmsValue.Octets([2]))).Error);
        Assert.Same(assignments, ReadStoredValue(activation, 2));
        Assert.Same(masks, ReadStoredValue(activation, 3));
        Assert.Same(name, ReadStoredValue(activation, 4));

        ValueTask<OperationResult> Invoke(sbyte method, DlmsValue value) => ExecuteOperationAsync(device, activation,
            CosemOperationKind.Invoke, method, value);
        ValueTask<OperationResult> Write(sbyte index, DlmsValue value) => ExecuteOperationAsync(device, activation,
            CosemOperationKind.Write, index, value);
    }

    [Fact]
    public async Task PreservesAssignmentPositionsAndBoundsUnsignedIndexSpace()
    {
        var device = new EmclBuilder().Build("meter");
        var activation = device.Add(6, 0, LogicalName.Parse("0.0.14.0.0.255"));
        var register = device.Add(3, 0, LogicalName.Parse("1.0.1.8.0.255"));
        var reference = Reference(register);
        var assignments = new DlmsValue(DlmsType.Array, Enumerable.Repeat(reference, 255));
        await WriteValueAsync(device, activation, 2, assignments);
        var name = DlmsValue.Octets([255]);
        Assert.Equal(CosemError.Success, (await ExecuteOperationAsync(device, activation, CosemOperationKind.Invoke,
            2, Mask(name, 255))).Error);
        await WriteValueAsync(device, activation, 4, name);
        var handler = Assert.IsType<RegisterActivationHandler>(device.Handlers.Resolve("meter", activation));
        Assert.True(await handler.IsEnabledAsync(activation, register.LogicalName, TestContext.Current.CancellationToken));
        Assert.Equal(CosemError.ScopeOfAccessViolated, (await ExecuteOperationAsync(device, activation,
            CosemOperationKind.Invoke, 1, reference)).Error);
        Assert.Same(assignments, ReadStoredValue(activation, 2));
    }

    internal static DlmsValue Reference(CosemObject register) => DlmsValue.Structure(
        Number(DlmsType.UInt16, register.Descriptor.Key.ClassId), DlmsValue.Octets(register.LogicalName.ToBytes()));

    internal static DlmsValue Mask(DlmsValue name, params byte[] indices) => DlmsValue.Structure(name,
        new(DlmsType.Array, indices.Select(index => Number(DlmsType.UInt8, index))));
}
