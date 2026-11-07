[English](Installation.md) | **Русский**

# Руководство по установке

Realistic Life Simulator — скрипт для ScriptHookVDotNet v3, работающий в **сюжетном режиме GTA V на ПК**. Он не изменяет файлы игры, поэтому в папку скриптов игры кладётся только один файл — `RealisticLifeSimulator.dll`.

> Никогда не используйте скриптовые моды в GTA Online.

## 1. Требования

| Компонент | Где взять | Примечание |
|---|---|---|
| GTA V (ПК), сюжетный режим | Ваша лицензионная копия | Разработка велась на версии Legacy (DirectX 11), сборка 1.0.3351.0 |
| ScriptHookV + ASI-загрузчик | [dev-c.com/gtav/scripthookv](http://www.dev-c.com/gtav/scripthookv/) (только официальная страница) | Должен поддерживать вашу сборку игры |
| ScriptHookVDotNet **v3** | [github.com/scripthookvdotnet/scripthookvdotnet/releases](https://github.com/scripthookvdotnet/scripthookvdotnet/releases) | Разработка велась под API 3.7 / среду 3.6 |
| .NET Framework 4.8 | Центр обновления Windows / Microsoft | Обычно уже есть в Windows 10/11 |
| Visual C++ Redistributable (x64) | Microsoft | Нужен ScriptHookVDotNet |

В этом репозитории **нет** ScriptHookV, ScriptHookVDotNet и файлов игры.

## 2. Установка ScriptHookV

1. Скачайте версию, соответствующую сборке вашей игры.
2. Скопируйте `ScriptHookV.dll` и `dinput8.dll` из папки `bin` архива в папку GTA V (ту, где лежит `GTA5.exe`).

**Проверка:** запустите игру один раз. В папке GTA V появится файл `ScriptHookV.log` без ошибок.

## 3. Установка ScriptHookVDotNet v3

1. Скачайте последний релиз.
2. Скопируйте `ScriptHookVDotNet.asi`, `ScriptHookVDotNet2.dll`, `ScriptHookVDotNet3.dll` (и `.ini`, если он есть) в папку GTA V.
3. Если в папке GTA V нет папки `scripts`, создайте её.

**Проверка:** после запуска сюжетного режима в папке GTA V появится `ScriptHookVDotNet.log`; консоль SHVDN открывается клавишей F4.

## 4. Установка Realistic Life Simulator

1. Возьмите `RealisticLifeSimulator.dll` — со страницы [Releases](https://github.com/richbetterson/Realistic-Life-Simulator/releases) этого репозитория или соберите сами (раздел 7).
2. Скопируйте его в `GTA V\scripts\RealisticLifeSimulator.dll`.
3. *(Необязательно)* Скопируйте папки `Config` и `Data` из репозитория в `GTA V\RealisticLifeSimulator\`. Это не обязательно: при первом запуске мод создаст обе папки с теми же значениями по умолчанию.

Что получится:

```
Grand Theft Auto V\
├─ GTA5.exe
├─ ScriptHookV.dll, dinput8.dll                  (ScriptHookV)
├─ ScriptHookVDotNet.asi, ScriptHookVDotNet3.dll (ScriptHookVDotNet)
├─ scripts\
│   └─ RealisticLifeSimulator.dll                (этот мод)
└─ RealisticLifeSimulator\                       (мод создаёт при первом запуске)
    ├─ Config\    Settings.ini, KeyBindings.ini
    ├─ Data\      LifeHubs, GasStations, SpeedLimits, Properties, VehicleCatalog, Shops (.ini)
    ├─ Saves\     profile.xml + Backups\
    ├─ Logs\      RealisticLifeSimulator.log
    └─ Backups\   копии исправленных файлов настроек
```

## 5. Первый запуск

1. Запустите **сюжетный режим** и дождитесь, когда персонаж станет управляемым.
2. Один раз появится уведомление: *«Realistic Life Simulator установлен. Клавиши не назначены…»*.
3. Откройте карту и найдите **синий значок центра управления** (площадь Легиона, дом Майкла, дома Франклина, трейлер Тревора).
4. Встаньте в синий маркер и нажмите клавишу действия (по умолчанию **E**; подсказка на экране покажет нужную клавишу).
5. Меню управляется мышью: ЛКМ — выбрать, ПКМ или Esc — назад.

**Проверка:** в файле `GTA V\RealisticLifeSimulator\Logs\RealisticLifeSimulator.log` есть строка `Game-side start complete. Modules: 14`.

Что стоит сделать сначала:
- *Настройки > Язык*: «Авто», английский или русский.
- *Настройки > Профиль производительности*: на слабых ПК оставьте **Низкий**.
- *Клавиши*: при желании назначьте клавишу на «Открыть центр управления», чтобы открывать меню где угодно.

## 6. Обновление

1. Закройте игру.
2. Замените `GTA V\scripts\RealisticLifeSimulator.dll` новой версией.
3. Папку `GTA V\RealisticLifeSimulator\` не трогайте. Настройки, клавиши и сохранения обновятся автоматически: новые параметры добавятся со значениями по умолчанию, ваши значения сохранятся, а копия переписанного файла попадёт в `Backups\Config\`.

**Обновление с 0.5.x до 0.6.0:**
- Сохранение автоматически обновляется с формата 6 до 7 (поля статуса прав, объект, в котором вы находитесь). Ничего не удаляется.
- В `Settings.ini` появляются разделы `[Enforcement]`, `[FineAmounts]`, `[TrafficLights]`, `[SpeedLimiter]` и новые параметры в `[Police]` и `[Accidents]`. Меняется одно значение: `FineMode=Witnessed` (прежнее значение по умолчанию) один раз становится `Automatic`, потому что в режиме «При свидетелях» штрафы почти не выписывались. Если нужно, снова выберите «При свидетелях» в *Настройки > Штрафы и ПДД* — это сохранится.
- В `KeyBindings.ini` добавляется пустая строка `ToggleSpeedLimiter=`; назначенные вами клавиши сохраняются.
- При первом запуске правила прав применяются к уже имеющимся баллам: 24 и более активных балла — права «Отозваны» (восстановление через 3 часа игры в *Центр управления > Полиция и правовой статус*).
- Вне `GTA V\scripts\RealisticLifeSimulator.dll` и собственной папки мода новые файлы не устанавливаются.

**Откат на старую версию:** сохранение, записанное новой версией, старая версия откроет только для чтения, чтобы его не повредить. Если возвращаетесь к старому DLL, восстановите и папку `Saves` того времени (мод хранит копии в `Saves\Backups\`).

## 7. Сборка из исходного кода

Нужно: Windows с .NET Framework 4.x (в нём есть `csc.exe`), Git и ScriptHookVDotNet, установленный в папку GTA V. Visual Studio не нужна.

```powershell
cd "C:\Путь\К\Grand Theft Auto V"
git clone https://github.com/richbetterson/Realistic-Life-Simulator.git RealisticLifeSimulator
cd RealisticLifeSimulator
powershell -ExecutionPolicy Bypass -File Source\build.ps1            # компиляция + модульные тесты
powershell -ExecutionPolicy Bypass -File Source\build.ps1 -Install   # плюс копирование DLL в ..\scripts\ (игра закрыта)
```

Что делает `build.ps1`:
1. Компилирует `Source\Core\*.cs` и `Source\Game\*.cs` в `Source\bin\RealisticLifeSimulator.dll` со ссылкой на `..\ScriptHookVDotNet3.dll`. Поэтому репозиторий должен лежать прямо в папке GTA V.
2. Компилирует `Source\Core` вместе с `Source\Tests` в `Source\bin\RlsCoreTests.exe` и запускает тесты. Если хоть один тест не прошёл, сборка прерывается.
3. Копирует DLL в `Scripts\RealisticLifeSimulator.dll` внутри репозитория.
4. С ключом `-Install` копирует его в `GTA V\scripts\` (если GTA V запущена, отказывается).

В конце вывода должно быть `Passed: 40  Failed: 0`. Предупреждения об устаревших API ScriptHookVDotNet ожидаемы и безвредны.

## 8. Удаление

- Удалите `GTA V\scripts\RealisticLifeSimulator.dll`.
- Чтобы удалить и все данные, удалите `GTA V\RealisticLifeSimulator\`.

Больше мод ничего не меняет.
