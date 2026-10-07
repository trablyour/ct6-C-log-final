# TaskManager

Консольное приложение на C# для управления задачами с полноценной системой **логирования**, **трассировки** и **обработки ошибок**.

Приложение добавляет, удаляет и выводит задачи, а каждое ключевое событие, ошибка, время выполнения операции и ее контекст фиксируются в консоли, текстовых файлах и структурированном JSON-логе.

---

## Содержание

- [Быстрый старт](#быстрый-старт)
- [Возможности](#возможности)
- [Структура проекта](#структура-проекта)
- [Соответствие техническому заданию](#соответствие-техническому-заданию)
- [Логирование](#логирование)
- [Трассировка](#трассировка)
- [Обработка ошибок и исключений](#обработка-ошибок-и-исключений)
- [Оповещения](#оповещения)
- [Файлы логов](#файлы-логов)
- [Внешние системы: Seq, Sentry, email](#внешние-системы-seq-sentry-email)
- [Демонстрационный сценарий](#демонстрационный-сценарий)
- [История разработки](#история-разработки)

---

## Быстрый старт

Требуется **.NET SDK 7.0** или новее (для .NET 8 поменяйте `net7.0` на `net8.0` в `TaskManager.csproj`).

```bash
git clone <ссылка на репозиторий>
cd TaskManager
dotnet run
```

| Команда | Что делает |
|---|---|
| `dotnet run` | интерактивное меню |
| `dotnet run -- --demo` | автоматический сценарий: все операции, ошибки ввода, исключения, отчеты |
| `dotnet run -- --demo --fatal` | то же + критическая ошибка (Fatal) и аварийное завершение |
| `dotnet run -- --json-console` | логи в консоли в формате JSON |

### Меню

```
================== TaskManager ==================
  1. Добавить задачу
  2. Удалить задачу (по Id или названию)
  3. Показать список задач
  4. Отметить задачу выполненной
  5. Импорт задач из JSON-файла
  6. Отчеты (трассировка, ошибки, логи)
  7. Симуляция ошибок
  8. Трассировка в консоли: выключена
  0. Выход
```

---

## Возможности

**Задачи**
- добавление: название, описание, приоритет (низкий, средний, высокий);
- удаление по **Id** или по **названию** (если задач с таким названием несколько, программа попросит указать Id);
- вывод списка: Id, статус, приоритет, дата создания, название, описание;
- отметка выполнения, импорт из JSON-файла;
- хранение в `data/tasks.json`, при ошибке сохранения изменения откатываются.

**Диагностика**
- логирование через `ILogger<T>` + Serilog: консоль, текстовые файлы с ротацией, структурированный JSON;
- трассировка каждой операции через `TraceSource` (`Start` / `Stop`) и `Stopwatch`, связь вложенных операций через `Activity`;
- централизованная обработка исключений, уровни `Error` / `Fatal`, код ошибки для пользователя;
- оповещения: консоль (`!!! ОШИБКА !!!`), файл, email, Sentry;
- встроенные отчеты: статистика операций, дерево трасс, журнал ошибок, статистика логов.

---

## Структура проекта

```
TaskManager/
├── Program.cs                     точка входа: настройка, DI, глобальный try/catch, завершение сеанса
├── TaskManager.csproj
│
├── Models/
│   └── TaskItem.cs                задача, приоритет, результат операции
├── Storage/
│   └── JsonTaskRepository.cs      хранение в JSON, обертка ошибок в StorageException
├── Services/
│   └── TaskService.cs             операции: AddTask, RemoveTask, ListTasks, CompleteTask, ImportTasks
│
├── Logging/
│   ├── LoggingSetup.cs            конфигурация Serilog (консоль, файлы, JSON, Seq, Sentry)
│   └── LogEvents.cs               коды событий (EventId) и идентификатор сеанса
├── Diagnostics/
│   ├── AppTracing.cs              TraceSource и ActivitySource
│   ├── OperationScope.cs          трассировка одной операции: Start -> шаги -> Stop
│   ├── TraceListeners.cs          слушатели TraceSource: файл и цветная консоль
│   ├── TraceSetup.cs              подключение слушателей и фильтров
│   └── TraceCollector.cs          сбор завершенных операций (Activity) в отчет и JSON
├── ErrorHandling/
│   ├── Exceptions.cs              TaskManagerException, StorageException, CriticalException
│   ├── ErrorHandler.cs            централизованное логирование исключений
│   └── ErrorContext.cs            путь операций, в которых произошла ошибка
├── Alerts/
│   └── Alerts.cs                  оповещения: консоль, файл, email
├── Reports/
│   └── LogAnalyzer.cs             чтение структурированного лога для отчетов
└── UI/
    ├── ConsoleUI.cs               меню, диалоги, отчеты, демо-сценарий
    └── ConsoleOutput.cs           цветной вывод
```

---

## Соответствие техническому заданию

| Требование | Реализация |
|---|---|
| **Добавление**: название, описание, приоритет | `TaskService.AddTask` |
| лог «Добавлена задача {TaskTitle}», `{@Task}` | поле `TaskTitle` + вся задача в поле `Task` (scope `@Task`) |
| трассировка: начало, конец, время | `OperationScope`: `TraceEventType.Start` / `Stop` + `Stopwatch` |
| ошибка при пустом названии | проверка, `Warning` в лог, результат `ПРЕДУПРЕЖДЕНИЕ` в трассе |
| **Удаление** по названию или Id | `TaskService.RemoveTask` |
| «Задача "{TaskTitle}" удалена» / «Ошибка: задача не найдена» | `LogInformation` / `LogError` |
| **Вывод списка** | `TaskService.ListTasks` |
| «Показан список из {Count} задач» | `LogInformation(TasksListed, ...)` |
| **Выход**: «Программа завершается», конец сеанса | `finally` в `Main` + `TraceEventType.Stop` сеанса |
| **Serilog**: консоль | `WriteTo.Console` (текст или `RenderedCompactJsonFormatter` с `--json-console`) |
| **Serilog**: файл с ротацией | `WriteTo.File(..., rollingInterval: Day, rollOnFileSizeLimit: true)` |
| **Serilog**: JSON | `WriteTo.File(new RenderedCompactJsonFormatter(), "logs/structured-.json")` |
| Seq / внешние системы (опционально) | `Serilog.Sinks.Seq`, `Sentry.Serilog` |
| **TraceSource + Stopwatch** | `AppTracing.Source.TraceEvent(Start)` → `Stopwatch` → `TraceEvent(Stop, ... мс)` |
| **try/catch в Main** → `Log.Fatal(ex, "Необработанная ошибка")` | `Program.Main` |
| **try/catch в AddTask, RemoveTask** → `Error` | каждый метод `TaskService` |
| сообщение «Произошла ошибка. Подробнее см. логи.» | `ErrorHandler.UserMessage` + код ошибки |
| базовое логирование | абстракция `ILogger<T>` (Microsoft.Extensions.Logging), `EventId`, уровни |

---

## Логирование

Все классы получают `ILogger<T>` через внедрение зависимостей (`Microsoft.Extensions.DependencyInjection`). Провайдером служит **Serilog** (`Serilog.Extensions.Logging`), поэтому код не зависит от конкретной библиотеки логирования.

```csharp
services.AddLogging(builder =>
{
    builder.ClearProviders();
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddSerilog(dispose: false);
});
```

### Приемники (sinks)

| Куда | Формат | Уровень |
|---|---|---|
| консоль | цветной текст или JSON (`--json-console`) | Information+ |
| `logs/log-ГГГГММДД.txt` | текст, ротация по дням и по 10 МБ, хранится 14 файлов | Debug+ |
| `logs/errors-ГГГГММДД.txt` | текст со стеком вызовов | Warning+ |
| `logs/structured-ГГГГММДД.json` | JSON (CLEF), ротация по дням и по 10 МБ | Debug+ |
| Seq | структурированные события | Debug+ (если задан `TASKMANAGER_SEQ_URL`) |
| Sentry | ошибки | Error+ (если задан `TASKMANAGER_SENTRY_DSN`) |

### Уровни

| Уровень | Примеры |
|---|---|
| Debug | шаги внутренних операций, сохранение файла, выбор пункта меню |
| Information | запуск и завершение, задача добавлена / удалена / выполнена, показан список |
| Warning | пустое название, некорректный ввод, несколько задач с одним названием |
| Error | задача не найдена, исключения в операциях (поврежденный файл, файл занят, ошибка в коде) |
| Critical (Fatal) | необработанное исключение в `Main`, нарушение целостности данных |

### Коды событий (`Logging/LogEvents.cs`)

| Диапазон | Назначение | Примеры |
|---|---|---|
| 1xxx | приложение | 1000 `AppStarted`, 1001 `AppStopping`, 1002 `AppCancelled` |
| 2xxx | операции с задачами | 2001 `TaskAdded`, 2002 `TaskRemoved`, 2003 `TasksListed` |
| 3xxx | ошибки ввода | 3001 `ValidationFailed`, 3002 `TaskNotFound` |
| 4xxx | хранилище | 4001 `StorageLoaded`, 4003 `StorageError` |
| 5xxx | трассировка | 5001 `OperationFinished` |
| 6xxx | исключения и оповещения | 6001 `ExceptionCaught`, 6002 `AlertSent` |

### Структурированная запись

Пример строки из `logs/structured-ГГГГММДД.json`:

```json
{
  "@t": "2026-10-07T11:02:11.4521Z",
  "@m": "Добавлена задача \"Купить продукты\" (Id 2, приоритет Low)",
  "@i": "7c1e2a90",
  "TaskTitle": "Купить продукты",
  "TaskId": 2,
  "Priority": "Low",
  "Task": { "Id": 2, "Title": "Купить продукты", "Description": "Молоко, хлеб", "Priority": "Low", "IsDone": false, "CreatedAt": "2026-10-07T14:02:11" },
  "EventId": { "Id": 2001, "Name": "TaskAdded" },
  "SourceContext": "TaskManager.Services.TaskService",
  "Operation": "AddTask",
  "OperationId": 7,
  "TraceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "SpanId": "00f067aa0ba902b7",
  "Application": "TaskManager",
  "Version": "1.0.0",
  "SessionId": "5f3c9a1e",
  "MachineName": "DESKTOP-PC"
}
```

---

## Трассировка

Каждая операция оборачивается в `OperationScope`:

```csharp
using var op = _tracer.Start("AddTask");      // TraceEventType.Start + запуск Stopwatch

op.Step("проверка названия и описания");      // TraceEventType.Verbose
...
op.Success($"задача #{task.Id}");             // TraceEventType.Stop + время выполнения
```

Внутри:

1. до выполнения: `TraceSource.TraceEvent(TraceEventType.Start, id, ...)`;
2. запуск `Stopwatch`, выполнение шагов (`TraceEventType.Verbose`);
3. после: `TraceEventType.Stop` с временем в миллисекундах и результатом;
4. результат: **УСПЕХ**, **ПРЕДУПРЕЖДЕНИЕ** (ошибка ввода, `TraceEventType.Warning`) или **ОШИБКА** (исключение, `TraceEventType.Error`). Если операция прервана исключением и результат не задан, она автоматически помечается как ошибочная.

Дополнительно каждая операция создает `Activity` (`System.Diagnostics`): вложенные операции, например `SaveTasks` внутри `AddTask`, связываются через `TraceId` / `ParentSpanId`, а все логи внутри операции получают поля `Operation`, `TraceId`, `SpanId`.

Сеанс работы программы тоже трассируется: `Start` при запуске и `Stop` при завершении с длительностью и кодом выхода.

### Слушатели TraceSource

| Слушатель | Фильтр | Куда |
|---|---|---|
| `FileTraceListener` | все события | `logs/trace-ГГГГММДД.txt` |
| `ColorConsoleTraceListener` | `EventTypeFilter`, настраивается в меню (п. 8): выкл / Start-Stop / подробно | консоль |

### Пример трассы

```
14:02:11.430 START  #0    Сеанс 5f3c9a1e: начало
14:02:11.441 START  #7    AddTask: начало
14:02:11.441 STEP   #7        шаг 1: проверка названия и описания
14:02:11.442 STEP   #7        шаг 2: создание задачи
14:02:11.442 STEP   #7        шаг 3: сохранение в файл
14:02:11.443 START  #8        SaveTasks: начало
14:02:11.443 STEP   #8            шаг 1: сериализация в JSON
14:02:11.446 STEP   #8            шаг 2: запись в файл
14:02:11.449 STOP   #8        SaveTasks: конец, УСПЕХ, 5.87 мс (сохранено 2)
14:02:11.452 STOP   #7    AddTask: конец, УСПЕХ, 11.20 мс (задача #2)
14:02:11.460 START  #9    AddTask: начало
14:02:11.460 STEP   #9        шаг 1: проверка названия и описания
14:02:11.461 WARN   #9        предупреждение: пустое название
14:02:11.461 STOP   #9    AddTask: конец, ПРЕДУПРЕЖДЕНИЕ, 0.41 мс
...
14:05:40.012 STOP   #0    Сеанс 5f3c9a1e: конец, 208.6 с, код 0
```

Завершенные операции также пишутся в `logs/traces-ГГГГММДД.json` (имя, время, длительность, результат, шаги, теги, связи) и показываются в отчете (меню 6 → 1): таблица вызовов по операциям и дерево последних трасс.

---

## Обработка ошибок и исключений

### Где перехватываются исключения

```
Main (try/catch)                       -> Log.Fatal("Необработанная ошибка"), код выхода 1
 └─ ConsoleUI: ErrorHandler.Execute    -> Error, программа продолжает работу
     └─ TaskService.AddTask / RemoveTask / ... (try/catch)
                                       -> Error, откат изменений, "Произошла ошибка. Подробнее см. логи."
         └─ JsonTaskRepository         -> IOException / JsonException оборачиваются в StorageException

AppDomain.UnhandledException           -> Fatal (исключения в других потоках)
TaskScheduler.UnobservedTaskException  -> Error (необработанные ошибки в Task)
```

| Исключение | Где возникает | Уровень | Что происходит |
|---|---|---|---|
| `StorageException` (`IOException`) | файл данных занят | Error | ловится в `AddTask`, задача не добавляется |
| `StorageException` (`JsonException`) | импорт поврежденного файла | Error | ловится в `ImportTasks` |
| `StorageException` при запуске | поврежденный `tasks.json` | Error | файл переносится в резервную копию, программа стартует с пустым списком |
| `NullReferenceException` | ошибка в коде | Error | ловится централизованно в `ErrorHandler.Execute` |
| `CriticalException` | нарушение целостности | Fatal | доходит до `Main`, программа завершается |

### Что записывается об исключении

| Поле | Содержимое |
|---|---|
| `ExceptionMessage` | текст ошибки (`Exception.Message`) |
| `StackTrace` | стек вызовов (`Exception.StackTrace`) |
| `ExceptionType` | полное имя типа |
| `InnerExceptions` | цепочка внутренних исключений |
| `OperationPath` | какая операция выполнялась, с вложенностью: `AddTask > SaveTasks` |
| `ErrorTraceId` | связь с трассой |
| `ErrorContext` | контекст: название задачи, путь к файлу, строка ошибки в JSON и т.д. |
| `ErrorId` | короткий код, который видит пользователь |
| уровень | `Error` или `Fatal` |

Пользователь видит короткое сообщение:

```
✗ Произошла ошибка. Подробнее см. логи. Код ошибки: A1B2C3
```

По коду `A1B2C3` запись быстро находится в любом лог-файле.

---

## Оповещения

| Канал | Уровень | Как включить |
|---|---|---|
| консоль: блок `!!! ОШИБКА !!!` | Error+ | всегда |
| файл `logs/alerts-ГГГГММДД.txt` | Error+ | всегда |
| email | Fatal | переменные окружения (см. ниже) |
| Sentry | Error+ | переменная `TASKMANAGER_SENTRY_DSN` |

```
!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
!!!  ОШИБКА  (код A1B2C3)
!!!  Операция: AddTask > SaveTasks
!!!  StorageException: Не удалось сохранить задачи
!!!  Причина:  IOException: The process cannot access the file ...
!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
```

Одинаковые ошибки (тот же тип в той же операции) оповещают не чаще раза в 30 секунд, в лог при этом пишутся все. Сбой самого оповещения (например, недоступен почтовый сервер) не роняет программу.

---

## Файлы логов

Создаются в папке `logs/` рядом с проектом:

| Файл | Формат | Содержимое |
|---|---|---|
| `log-ГГГГММДД.txt` | текст | все записи начиная с Debug |
| `errors-ГГГГММДД.txt` | текст | предупреждения и ошибки со стеком вызовов |
| `structured-ГГГГММДД.json` | JSON (CLEF) | все записи со всеми полями |
| `trace-ГГГГММДД.txt` | текст | трассировка TraceSource: Start / шаги / Stop |
| `traces-ГГГГММДД.json` | JSON | завершенные операции (Activity) |
| `alerts-ГГГГММДД.txt` | текст | оповещения об ошибках |

---

## Внешние системы: Seq, Sentry, email

Все необязательно и включается переменными окружения. Без них программа работает как обычно.

### Seq (веб-интерфейс для поиска по логам)

```bash
docker run -d --name seq -e ACCEPT_EULA=Y -p 5341:80 datalust/seq
```

```powershell
# PowerShell
$env:TASKMANAGER_SEQ_URL = "http://localhost:5341"
dotnet run
```

Логи будут доступны на http://localhost:5341, по ним можно искать запросами вроде `TaskTitle = 'Купить продукты'` или `@Level = 'Error'`.

### Sentry

```powershell
$env:TASKMANAGER_SENTRY_DSN = "https://<ключ>@<организация>.ingest.sentry.io/<проект>"
```

### Email при критической ошибке

```powershell
$env:TASKMANAGER_SMTP_HOST = "smtp.gmail.com"
$env:TASKMANAGER_SMTP_PORT = "587"
$env:TASKMANAGER_SMTP_USER = "you@gmail.com"
$env:TASKMANAGER_SMTP_PASSWORD = "<пароль приложения>"
$env:TASKMANAGER_ALERT_EMAIL = "admin@example.com"
```

---

## Демонстрационный сценарий

`dotnet run -- --demo` выполняет по шагам:

1. добавление трех задач (с описаниями и разными приоритетами);
2. добавление с пустым названием: **ошибка ввода**;
3. вывод списка;
4. удаление задачи **по названию**;
5. удаление несуществующей задачи: «Ошибка: задача не найдена»;
6. импорт поврежденного файла: исключение ловится в `ImportTasks`;
7. добавление при занятом файле: исключение ловится в `AddTask`, изменения откатываются;
8. ошибка в коде (`NullReferenceException`): ловится централизованно;
9. повторный вывод списка: программа продолжает работать;
10. отчет по трассировке;
11. журнал ошибок.

С флагом `--fatal` добавляется шаг 12: критическая ошибка, `Log.Fatal`, оповещение и завершение с кодом 1.

---

## История разработки

| Этап | Что добавлено |
|---|---|
| КТ1 | базовые операции, логирование через `Microsoft.Extensions.Logging`, простая трассировка |
| КТ2 | Serilog: консоль, файлы с датами |
| КТ3 | структурированные логи в JSON, обогащение записей, анализ логов |
| КТ4 | трассировка операций с результатом (успех / предупреждение / ошибка) |
| КТ5 | централизованная обработка исключений, оповещения |
| **Итог** | объединение всего: `ILogger<T>` + Serilog, `TraceSource` + `Stopwatch` + `Activity`, удаление по названию, описание задач, откат изменений при ошибках, Seq |

## Технологии

.NET 7 / C# 11 · Microsoft.Extensions.Logging · Microsoft.Extensions.DependencyInjection · Serilog (Console, File, Compact JSON, Seq) · Sentry · System.Diagnostics (TraceSource, Stopwatch, Activity)
