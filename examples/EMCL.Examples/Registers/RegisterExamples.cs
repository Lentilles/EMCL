using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;
using EMCL.Objects.Registers;

namespace EMCL.Examples.Registers;

internal static class RegisterExamples
{
    internal static async Task RunAsync(EmclDevice device)
    {
        var extended = device.Add(CosemClassIds.ExtendedRegister, ExtendedRegisterMembers.Version0, LogicalName.Parse("1.0.3.8.0.255"));
        var extendedHandler = (ExtendedRegisterHandler)device.Handlers.Resolve(device.Catalog.LogicalDeviceId, extended);
        EnsureSuccess(await extendedHandler.CaptureAsync(extended, DlmsValue.Number(DlmsType.UInt32, 500)));
        var captureTime = await Execute(CosemOperationKind.Read, extended, ExtendedRegisterMembers.Attributes.CaptureTime);
        EnsureSuccess(captureTime);
        Console.WriteLine($"Extended Register captured at: {CosemDateTime.FromValue(captureTime.Value!).ToDateTimeOffset():O}");

        var demand = device.Add(CosemClassIds.DemandRegister, DemandRegisterMembers.Version0, LogicalName.Parse("1.0.1.6.0.255"));
        EnsureSuccess(await Execute(CosemOperationKind.Write, demand, DemandRegisterMembers.Attributes.Period, DlmsValue.Number(DlmsType.UInt32, 60)));
        var demandHandler = (DemandRegisterHandler)device.Handlers.Resolve(device.Catalog.LogicalDeviceId, demand);
        EnsureSuccess(await demandHandler.RecordSampleAsync(demand, DlmsValue.Number(DlmsType.UInt32, 10)));
        EnsureSuccess(await demandHandler.RecordSampleAsync(demand, DlmsValue.Number(DlmsType.UInt32, 30)));
        // The application schedules period transitions; the handler owns no timer.
        EnsureSuccess(await Execute(CosemOperationKind.Invoke, demand, DemandRegisterMembers.Methods.NextPeriod, DlmsValue.Number(DlmsType.Int8, 0)));
        var lastAverage = await Execute(CosemOperationKind.Read, demand, DemandRegisterMembers.Attributes.LastAverageValue);
        EnsureSuccess(lastAverage);
        Console.WriteLine($"Demand Register last average: {lastAverage.Value!.Value}");

        var activation = device.Add(CosemClassIds.RegisterActivation, RegisterActivationMembers.Version0, LogicalName.Parse("0.0.14.0.0.255"));
        var reference = DlmsValue.Structure(DlmsValue.Number(DlmsType.UInt16, CosemClassIds.ExtendedRegister), DlmsValue.Octets(extended.LogicalName.ToBytes()));
        EnsureSuccess(await Execute(CosemOperationKind.Invoke, activation, RegisterActivationMembers.Methods.AddRegister, reference));
        var maskName = DlmsValue.Octets([1]);
        var mask = DlmsValue.Structure(maskName, DlmsValue.Array(DlmsValue.Number(DlmsType.UInt8, 1)));
        EnsureSuccess(await Execute(CosemOperationKind.Invoke, activation, RegisterActivationMembers.Methods.AddMask, mask));
        EnsureSuccess(await Execute(CosemOperationKind.Write, activation, RegisterActivationMembers.Attributes.ActiveMask, maskName));
        var activationHandler = (RegisterActivationHandler)device.Handlers.Resolve(device.Catalog.LogicalDeviceId, activation);
        Console.WriteLine($"Extended Register enabled: {await activationHandler.IsEnabledAsync(activation, extended.LogicalName)}");

        ValueTask<OperationResult> Execute(CosemOperationKind kind, CosemObject target, sbyte index, DlmsValue? value = null) =>
            device.Pipeline.ExecuteAsync(new(kind, target.Descriptor.Key.ClassId, target.LogicalName, index, value), device.LocalContext);
    }

    private static void EnsureSuccess(OperationResult result)
    {
        if (result.Error != CosemError.Success)
        {
            throw new InvalidOperationException($"Register example failed: {result.Error}: {result.Diagnostic}");
        }
    }
}
