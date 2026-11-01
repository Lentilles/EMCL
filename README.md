# EMCL

Расширяемая библиотека DLMS/COSEM для .NET 10. Универсальная модель объектов, бинарные кодеки, локальное выполнение операций, клиентская LN-сессия и независимые адаптеры кадров и транспорта.

Библиотечные сборки не имеют сторонних NuGet-зависимостей. Тесты используют xUnit v3 и Microsoft Testing Platform. Для сборки нужен стабильный .NET SDK 10; политика выбора SDK и тестового движка задана в `global.json`.

```sh
dotnet restore EMCL.slnx --disable-parallel
dotnet build EMCL.slnx -c Release --no-restore -m:1
dotnet test --project tests/EMCL.Tests/EMCL.Tests.csproj -c Release --no-build --no-restore
dotnet run --project examples/EMCL.Examples -c Release --no-build -- --demo
```

`EMCL.Tests` содержит тесты `[Fact]` и параметризованные `[Theory]`, разделённые по подсистемам. Эталонные сообщения проверяются как отдельные тестовые сценарии. Анализаторы xUnit включены; асинхронные тесты используют токен отмены тестового контекста.

Локальные NuGet-пакеты можно собрать без публикации в NuGet.org:

```sh
dotnet build EMCL.slnx -c Release --no-restore -m:1
dotnet pack EMCL.slnx -c Release --no-restore --no-build --disable-build-servers -m:1 \
  -p:PackageVersion=0.1.0-preview.1 -p:PackageOutputPath="$PWD/packages"
```

В `packages/` появятся `EMCL.0.1.0-preview.1.nupkg` и пакеты всех библиотечных сборок, от которых он зависит. Пример и тесты не упаковываются; папка `packages/` исключена из Git. Для использования из другого проекта добавьте папку `packages/` как локальный источник NuGet и установите пакет `EMCL` той же версии. Нужен установленный .NET 10 SDK и совместимая целевая платформа. Команда `dotnet pack` только создаёт файлы; публикация выполняется отдельной командой `dotnet nuget push`.

Публичный API документирован XML-комментариями на английском. XML-файлы генерируются рядом с DLL и включаются в NuGet-пакеты. Отсутствие документации (`CS1591`), предупреждения компилятора и включённых анализаторов блокируют сборку. Правила разработки и команды проверки описаны в [CONTRIBUTING.md](CONTRIBUTING.md).

## Экспорт ассоциации реального прибора

Консольный пример подключается по Wrapper/TCP или через HDLC/TCP-шлюз, читает `object_list` (в том числе GET-блоками) и сохраняет JSON с OBIS-кодами, классами, атрибутами, методами и исходными/расшифрованными правами доступа.

```sh
cp examples/EMCL.Examples/meter.example.json examples/EMCL.Examples/meter.local.json
dotnet run --project examples/EMCL.Examples -c Release
```

Для запуска из IDE достаточно выбрать `EMCL.Examples` как запускаемый проект и нажать **Run**. Без аргументов приложение предпочитает `meter.local.json`, затем `meter.example.json`. Перед запуском отредактируйте транспорт, адреса, TCP-порт, версию ассоциации и аутентификацию в конфиге. Полный путь результата печатается в консоли. Путь результата и разрешение перезаписи также задаются в конфиге. Относительный путь считается от каталога копии конфига рядом с приложением. Поля пароля не включаются в экспорт.

[Параметры конфигурации и схема JSON](examples/EMCL.Examples/README.md) · [Шаблон конфигурации](examples/EMCL.Examples/meter.example.json).

## Структура репозитория

```text
src/                         Библиотечные проекты
  EMCL.Core/                 Access, Codecs, Descriptors, Objects, Operations,
                             Pipeline, State, Validation, Values
  EMCL.Protocol/             Acse, Apdu, Axdr, BlockTransfer, Internal
  EMCL.Client/               Association, Exceptions, Internal, Sessions
  EMCL.Objects/              Association, Catalog, Clock, Definitions, Handlers,
                             Internal, Processing, ProfileGeneric, Registers
  EMCL.Framing.Hdlc/         Frames, Llc, Segmentation
  EMCL.Framing.Wrapper/
  EMCL.Transport/            Channels, Streams
  EMCL/                      Composition, Framing, Messaging
tests/EMCL.Tests/            Проверки по подсистемам; общие данные и doubles в Support
examples/EMCL.Examples/      Исполняемые примеры
docs/                       Матрица возможностей и источники
```

Каталоги группируют файлы по назначению. Namespace соответствует корневому namespace проекта и относительному пути: например, `src/EMCL.Core/Access/AttributeAccess.cs` использует `EMCL.Core.Access`, а `src/EMCL.Protocol/Acse/AcseCodec.cs` — `EMCL.Protocol.Acse`. Соответствие проверяется анализатором `IDE0130` при сборке.

## Сборки

| Сборка | Назначение |
| --- | --- |
| `EMCL.Core` | Идентификаторы, значения, дескрипторы, состояние и контракты расширения |
| `EMCL.Protocol` | A-XDR, BER/ACSE, LN APDU, сборка GET-блоков |
| `EMCL.Client` | Клиентская LN-ассоциация, GET/SET/ACTION, invoke ID, сроки выполнения и загрузка каталога |
| `EMCL.Objects` | Каталог, импорт ассоциации, диспетчер, локальные обработчики |
| `EMCL.Framing.Hdlc` | Кадры type 3, CRC, LLC, сборка сегментов |
| `EMCL.Framing.Wrapper` | Wrapper v1 |
| `EMCL.Transport` | `IByteTransport`, `IApduChannel`, `TcpByteTransport`, `StreamByteTransport`, канал с внедряемым кодеком кадров |
| `EMCL` | Компоновка определений классов, адаптеров, Wrapper/HDLC APDU-каналов и endpoint |

`Protocol` не ссылается на транспорт, HDLC или Wrapper. Стандартные определения и обработчики находятся в `Objects`; каждый составленный прибор получает собственные экземпляры обработчиков.

```text
IByteTransport
  → IIncrementalCodec<Frame>
  → IFramePayloadAdapter<Frame>       HDLC: сегменты → LLC; Wrapper: payload
  → IApduChannel
  → IIncrementalCodec<DlmsApdu>
  → IDlmsPipeline                    middleware → доступ → проверка → обработчик
```

Обратное направление использует те же кодеки. `GetBlockAssembler` собирает данные xDLMS отдельно от сегментации HDLC.

## Быстрый пример

```csharp
using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Definitions;

var device = new EmclBuilder().Build("meter-1");
var register = device.Add(CosemClassIds.Register, RegisterMembers.Version0, LogicalName.Parse("1.0.1.8.0.255"));
register.State.Set(RegisterMembers.Attributes.Value, DlmsValue.Number(DlmsType.UInt32, 12345));

var result = await device.Pipeline.ExecuteAsync(
    new(CosemOperationKind.Read, CosemClassIds.Register, register.LogicalName, RegisterMembers.Attributes.Value),
    device.LocalContext);
Console.WriteLine(result.Value!.AsUInt64());
```

Модель универсальная: class ID, версия, logical name, дескриптор и значения по индексам. `ClassKey` содержит class ID и точную версию; определения можно заменить через `ClassRegistry`. По умолчанию время в Clock, Extended Register и Demand Register кодируется как `DlmsType.OctetString`; для прибора с тегом `DlmsType.DateTime` зарегистрируйте соответствующие определения через `ClockClassDefinition`, `ExtendedRegisterClassDefinition` и `DemandRegisterClassDefinition` до импорта или `Build`. Разные наборы определений создавайте в отдельных `EmclBuilder`. Одно логическое устройство использует одну версию каждого класса; разные устройства независимы.

`DlmsValue` сохраняет DLMS-тег. Enum и unsigned, array и structure различаются. Байты и коллекции копируются в неизменяемое представление. `CosemDateTime` сохраняет все 12 октетов, включая специальные поля, deviation и status; вызов `ToDateTimeOffset()` допустим только для конкретной даты со смещением.

В коде приложения доступны именованные идентификаторы: `CosemClassIds.Register`, `RegisterMembers.Version0`, `RegisterMembers.Attributes.Value`, `RegisterMembers.Methods.Reset`. Теги APDU представлены `ApduTag`, режимы доступа — `AttributeAccessModeV1`, `MethodAccessModeV1` и `AccessModeV3`. Идентификаторы не требуют приведения к enum при создании пользовательских классов: модель по-прежнему принимает произвольные class ID и индексы.

## Импорт ассоциации

Файл содержит непосредственно одно A-XDR-значение `object_list`: без GET-response, Wrapper, LLC и HDLC. Поток читается до EOF, лишние байты и незавершённые данные отклоняются. Для сетевого потока сначала получите полное значение из APDU или GET-блоков.

```csharp
using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Objects.Definitions;

var builder = new EmclBuilder();
await using var file = File.OpenRead("object-list.bin");
var imported = await builder.CreateImporter().ImportAsync(
    file, "meter-1", "public-client", associationVersion: AssociationLnMembers.Version1);
var device = builder.Build("meter-1", imported);

var response = await device.Pipeline.ExecuteAsync(
    new(CosemOperationKind.Read, CosemClassIds.Register, LogicalName.Parse("1.0.1.8.0.255"), RegisterMembers.Attributes.Value),
    device.CreateContext());
```

Импорт создаёт новый каталог атомарно; значения атрибутов в `object_list` отсутствуют. Чтение незагруженного значения возвращает `ObjectUnavailable`. Повторяющиеся logical name, неверная структура и разные версии одного класса внутри устройства отклоняются.

Версия Association LN задаётся явно: права v1 — перечисления, v3 — битовые требования. Они сохраняются в `AssociationAccessContext`, отдельно от объектов. Если импорт относится к объекту ассоциации с именем, отличным от `0.0.40.0.0.255`, передайте `associationObject` в `Build`.

`CreateContext()` получает актуальный снимок прав. После локальных Association add/remove создайте новый контекст; ранее выданные снимки неизменяемы. Удаление объекта из одной ассоциации удаляет его видимость, сохраняя общий объект устройства.

Контекст без ассоциации (`LocalContext`) предназначен для локального использования и не ограничивает доступ правами ассоциации. Контекст с ассоциацией проверяет права; отсутствующее разрешение означает отказ. Признаки аутентификации, шифрования и подписи устанавливает доверенный слой приложения после соответствующих проверок. Сами кодеки ACSE не подтверждают подлинность.

## Свои объекты и обработчики

Неизвестная пара class ID/версия сохраняется без автоматического выбора ближайшей версии. Из `object_list` известны идентификаторы и права, но не типы атрибутов или алгоритмы методов. Добавьте дескриптор явно:

```csharp
using EMCL.Core.Descriptors;
using EMCL.Core.Values;

var key = new ClassKey(9000, 2);
builder.Definitions.Register(new CosemClassDescriptor(
    key, "Manufacturer object",
    [new(1, "logical_name", DlmsType.OctetString), new(2, "reading", DlmsType.UInt16)],
    [new(1, "command", DlmsType.UInt8)]));

// Для ранее импортированных экземпляров:
device.Catalog.Rebind(builder.Definitions);
```

Реализуйте `ICosemObjectHandler` или унаследуйте `StoredValueHandler`, переопределив нужную операцию. Полный компилируемый пример находится в `EMCL.Examples`.

```csharp
device.Handlers.ForClass(key, myHandler);
device.Handlers.ForInstance("meter-1", LogicalName.Parse("0.0.128.1.0.255"), specialHandler);
```

Приоритет: обработчик экземпляра → пользовательский обработчик класса и версии → стандартный → универсальный. Приоритет экземпляра включает идентификатор логического устройства.

Для замены одного атрибута или метода оберните действующий обработчик:

```csharp
using EMCL.Core.Operations;
using EMCL.Core.Values;
using EMCL.Objects.Handlers;
using EMCL.Objects.Definitions;

var target = device.Catalog.Find(LogicalName.Parse("1.0.1.8.0.255"))!;
var original = device.Handlers.Resolve(device.Catalog.LogicalDeviceId, target);
var overridden = new ObjectHandlerOverrides(original)
    .OnRead(RegisterMembers.Attributes.Value, (obj, operation, context, ct) =>
        ValueTask.FromResult(OperationResult.Ok(DlmsValue.Number(DlmsType.UInt32, 42))));
device.Handlers.ForInstance(device.Catalog.LogicalDeviceId, target.LogicalName, overridden);
```

Другие точки расширения:

- `IClassDefinitionProvider` — внешний каталог определений; последняя регистрация провайдера имеет приоритет, явное определение приоритетнее провайдеров.
- `ICosemObjectFactory` и `IObjectStateStore` — создание объектов и хранение состояния.
- `IValueConverter` — преобразование входных значений Write/Invoke перед валидацией.
- `IValueValidator` и `IAccessPolicy` — проверка операций и разрешений.
- `IDlmsMiddleware` — перехват операций в порядке регистрации; `device.Pipeline` — полная замена пайплайна операций.
- `IClockSource` — источник времени; `ICaptureValueSource` — получение захватываемых значений.
- `TimeProvider` и `IDemandValueCalculator` — время измерений и расчёт Demand Register; настраиваются через `MeasurementTimeProvider` и `DemandCalculatorFactory` в builder.
- `IIncrementalCodec<T>`, `IFramePayloadAdapter<T>`, `IByteTransport`, `IApduChannel` — независимая замена любого уровня обмена.

Настройте реестры до конкурентного использования. Стандартный диспетчер сериализует операции одного объекта. Захват нескольких объектов не является транзакционным снимком устройства; для такого требования внедрите `ICaptureValueSource` с нужной синхронизацией. Пользовательские хранилища и обработчики отвечают за собственную потокобезопасность.

## Транспорт и кадры

`ApduChannelSettings` хранит одну фабрику физического соединения и один выбор кадрирования. `ApduChannels.ConnectAsync` вызывает фабрику ровно один раз и возвращает готовый `IApduChannel`. Пример Wrapper/TCP:

```csharp
using EMCL.Core.Objects;
using EMCL.Core.Operations;
using EMCL.Framing;
using EMCL.Messaging;
using EMCL.Protocol.Apdu;
using EMCL.Objects.Definitions;
using EMCL.Transport.Streams;

var settings = new ApduChannelSettings(
    new TcpByteTransportFactory(host, 4059, TimeSpan.FromSeconds(5)),
    new ApduFraming.Wrapper(LocalPort: 16, RemotePort: 1));
await using var channel = await ApduChannels.ConnectAsync(settings, cancellationToken);
await using var endpoint = new DlmsEndpoint(channel);
const byte invokeIdAndPriority = 0xC1;
await endpoint.SendAsync(new LnRequest(invokeIdAndPriority,
    new(CosemOperationKind.Read, CosemClassIds.Register, LogicalName.Parse("1.0.1.8.0.255"), RegisterMembers.Attributes.Value)));
var apdu = await endpoint.ReceiveAsync();
```

Для HDLC замените только `Framing` на `new ApduFraming.Hdlc(HdlcAddress.Client(16), HdlcAddress.Server(1), TimeSpan.FromSeconds(5))`. Канал выполняет SNRM/UA, ведёт порядковые номера, подтверждает входящие сегменты RR и отправляет DISC при освобождении. Он не делает повторов и пока требует, чтобы исходящий APDU помещался в один согласованный information frame. Для низкоуровневого управления кадрами доступны `HdlcCodec` и `HdlcApduAdapter`.

Для собственного serial или иного соединения реализуйте `IByteTransportFactory.ConnectAsync`, возвращающий пользовательский `IByteTransport`, и передайте фабрику в `ApduChannelSettings.Connection`. `ReadAsync` транспорта возвращает число принятых байтов (ноль только при EOF), `WriteAsync` передаёт весь буфер либо выбрасывает исключение, `DisposeAsync` освобождает соединение. Канал владеет полученным транспортом. Для уже открытого потока доступен `StreamByteTransport` и низкоуровневые методы `ApduChannels.Wrapper`/`ConnectHdlcAsync`. Если источник уже отдаёт готовые APDU, реализуйте `IApduChannel` напрямую.

`IByteTransport.WriteAsync` обязан записать весь буфер либо завершиться исключением. `IApduChannel.ReceiveAsync` возвращает один завершённый APDU с принадлежащими результату байтами; `null` означает чистый EOF. Datagram-транспорт может реализовать `IApduChannel` напрямую. Канал кадров сохраняет неполные данные между чтениями, поддерживает несколько кадров за чтение и общие флаги HDLC. Обрыв кадра или сегментированного сообщения приводит к `EndOfStreamException`.

После ошибки кадров канал закрывает дальнейшую обработку с явной ошибкой; не выполняется скрытая ресинхронизация. Для собственного восстановления замените канал или адаптер. Endpoint остаётся низкоуровневым инструментом. Готовое управление ассоциацией и сопоставление ответов предоставляет `DlmsClientSession`.

## Клиентская LN-сессия

`DlmsClientSession` получает уже подключённый `IApduChannel`: прямой APDU-канал или результат `ApduChannels.ConnectAsync`. Консольный пример хранит соединение и кадрирование в одной конфигурации канала; библиотека открывает TCP-соединение и оставляет журналирование кадров приложению. Сессия находится в отдельной сборке `EMCL.Client`, которая не зависит от кадров.

```csharp
using EMCL.Client.Sessions;
using EMCL.Composition;
using EMCL.Core.Objects;
using EMCL.Objects.Definitions;

var builder = new EmclBuilder();
await using var client = new DlmsClientSession(channel);
var negotiated = await client.AssociateAsync(cancellationToken);
var value = await client.GetAsync(
    CosemClassIds.Register, LogicalName.Parse("1.0.1.8.0.255"),
    RegisterMembers.Attributes.Value, cancellationToken: cancellationToken);
var imported = await client.LoadCatalogAsync(
    builder.CreateImporter(), "meter-1",
    "public", AssociationLnMembers.Version3, cancellationToken: cancellationToken);
await client.ReleaseAsync(cancellationToken);
```

Для постепенного чтения `object_list` вызовите `OpenObjectListAsync(importer, associationVersion)`. Он возвращает одноразовый `AssociationObjectStream` после чтения заголовка массива: `TotalCount` содержит объявленное прибором число записей, а `CurrentCount` увеличивается при выдаче каждой записи через `await foreach`. Освобождайте поток через `await using`; завершение перечисления проверяет, что получены все записи и нет лишних данных. `LoadCatalogAsync` использует тот же поток и возвращает каталог только после полной проверки.

Доступны `SetAsync`, `ActionAsync` и `ExecuteAsync`. Удобные методы выбрасывают `DlmsOperationException` при ошибке прибора; `ExecuteAsync` возвращает `LnResponse` с исходным кодом результата и отдельным ACTION return-data error. Успешный GET с block transfer автоматически собирается в одно A-XDR-значение. GET-next подтверждает номер последнего принятого блока. Импорт каталога использует явно выбранную версию Association LN; текущие значения объектов из списка не выводятся.

По умолчанию сессия выполняет один запрос за раз. `MaxConcurrentRequests` разрешает до 16 независимых обменов, если прибор поддерживает такую нагрузку. Ответы сопоставляются по invoke ID, сервису и полному invoke-id-and-priority; передача сериализуется, чтение выполняется одним фоновым обработчиком. Таймаут `RequestTimeout` ограничивает целый обмен, включая все GET-блоки. Ожидание свободного места в очереди ограничивается токеном вызывающего кода. `TimeProvider` позволяет заменить источник сроков выполнения.

Отмена до отправки сохраняет ассоциацию. Отмена или таймаут после попытки отправки, неверный ответ, EOF или превышение входных лимитов переводит её в `Faulted` и завершает остальные ожидающие операции. Это предотвращает использование запоздалого ответа в новом обмене. Автоматических повторов нет. Сессия рассчитана на одну ассоциацию: после `Released` или `Faulted` создайте новую сессию на подходящем канале.

`ReleaseAsync` перестаёт принимать новые операции, дожидается начатых и выполняет RLRQ/RLRE. `DisposeAsync` отменяет и дожидается операций и ввода-вывода, затем освобождает канал; `LeaveOpen = true` оставляет канал приложению. Пользовательский канал должен поддерживать отмену чтения и записи. Повторный Dispose безопасен. После аварийного завершения переиспользование оставленного канала требует явного восстановления приложением.

Предложенный conformance проверяется по реализованному набору. Сервисы и selective access разрешены только после согласования. Максимальные размеры приёма клиента и сервера независимы; протокольный ноль означает отсутствие лимита, но `ProtocolLimits` всегда действуют. Ненулевые размеры ниже 12 зарезервированы. Буферы уведомлений и GET-блоков ограничены; начальный GET-блок должен иметь номер 1. `NotificationCapacity` не должна превышать `MaxCollectionItems`, а ненулевой рекламируемый размер приёма — `MaxMessageBytes`.

`ReadNotificationsAsync` принимает согласованные DataNotification независимо от ожидающих запросов. Приложение должно опустошать буфер; переполнение завершает сессию с явной ошибкой. Отмена перечисления уведомлений не отменяет сессию.

Настройки `LnAssociationOptions` поддерживают отсутствие аутентификации, LLS и HLS High (AES-128 challenge-response) через неизменяемые байты `Password`. HLS High вызывает метод Association LN и проверяет ответ до открытия сессии. Для другого механизма реализуйте `IClientAssociationNegotiator` и `IClientHighLevelAuthenticator`; для представления APDU — `IIncrementalCodec<DlmsApdu>`; весь клиент заменяется через `IDlmsClientSession`. Защищённые APDU требуют отдельной реализации. Готовое открытие Serial, полный жизненный цикл HDLC с ARQ и повторы остаются следующими этапами.

Полный исполняемый пример с локальным объектным пайплайном: [ClientSessionExample.cs](examples/EMCL.Examples/Client/ClientSessionExample.cs). Тестовый peer в примере демонстрирует обмен и не является готовой серверной сессией.

## Блоки, ограничения и локальное выполнение

Для GET block transfer вызовите `GetBlockAssembler.Begin` с ключом канала, устройства, ассоциации и invoke ID. Передавайте `GetBlockResponse` в `Accept`; после последнего блока декодируйте полученные байты через `AxdrCodec`. Повтор или пропуск блока завершает сборку ошибкой. `Abort` освобождает незавершённый обмен. Повторное использование invoke ID требует нового `Begin`.

`ProtocolLimits` ограничивает размер сообщений, глубину вложенности, число элементов и число блоков. Значения по умолчанию: 1 MiB, 64 уровня, 100 000 элементов, 4096 блоков. В компоновке вручную передавайте одинаковые параметры кодекам, сборщикам и каналу. Ограничения HDLC (2047 октетов без флагов) и Wrapper (65535 октетов payload) действуют дополнительно.

Локальные обработчики:

- Data: чтение/запись имеющихся значений.
- Register: чтение/запись и reset числового значения в ноль с сохранением типа. Для другого значения сброса зарегистрируйте `RegisterHandler` со своей функцией.
- Extended Register v0: фиксация значения/status и capture_time, reset с сохранением status. Тип времени задаётся определением класса.
- Demand Register v0: текущая/последняя средняя величина, reset/next_period, подача измерений и ограниченное окно выборок. Стандартный калькулятор — Float64 со средним одинаково взвешенных выборок; интегрирование энергии, вес по времени и округление задаются своим `IDemandValueCalculator`. Сбор измерений и переходы периодов запускает приложение.
- Register Activation v0: назначения регистров, создание/замена/удаление масок и выбор active_mask. Приложение запрашивает `IsEnabledAsync` перед измерением. Индексы маски начинаются с 1, максимум 255 назначений; выбранную маску можно удалить после выбора другой.
- Clock: частичная установка времени, корректировки, preset и shift через `IClockSource`. `MemoryClockSource` хранит частный сдвиг относительно `TimeProvider`, не изменяя часы ОС. Это локальная модель; планирование DST и физическое плавное управление часами предоставляются источником приложения через `ShiftTime`.
- Association LN: видимый список, add/remove объектов, управление пользователями v3; локальная серверная обработка HLS и изменение HLS-секрета требуют внешнего security handler.
- Profile Generic: capture/reset, FIFO/LIFO и четыре сортировки по значению, ёмкость, array/compact-array, селекторы диапазона и записей. Изменение capture_objects очищает буфер и зависимые профили. Автоматический периодический capture запускает приложение. Восстановление сокращённых значений `null-data` из предыдущих записей не выполняется неявно.

Подробная матрица возможностей и используемые источники — в [docs/COVERAGE.md](docs/COVERAGE.md).

Пример локального измерения Extended Register:

```csharp
using EMCL.Objects.Registers;
using EMCL.Objects.Definitions;

var extended = device.Add(CosemClassIds.ExtendedRegister, ExtendedRegisterMembers.Version0, LogicalName.Parse("1.0.3.8.0.255"));
var handler = (ExtendedRegisterHandler)device.Handlers.Resolve(
    device.Catalog.LogicalDeviceId, extended);
var captured = await handler.CaptureAsync(
    extended, DlmsValue.Number(DlmsType.UInt32, 500));
```

Producer API выполняется локально без проверки прав ассоциации и использует тот же объектный lock, что и диспетчер. Получайте обработчик до установки обёрток или сохраняйте ссылку на собственный экземпляр. Исполняемый пример всех трёх новых классов находится в [RegisterExamples.cs](examples/EMCL.Examples/Registers/RegisterExamples.cs).

Для приборов, передающих время тегом date-time (25), зарегистрируйте определения Extended/Demand Register с `DlmsType.DateTime`.
