; ต้องแก้ 2 บรรทัดนี้ก่อนคอมไพล์ถ้าโฟลเดอร์โปรเจกต์อยู่คนละที่:
;   - #define SourceDir  ให้ชี้ไปที่โฟลเดอร์ publish (มี SmartGateLPR1.exe กับ ai\ อยู่ข้างใน)
;   - #define MyAppVersion ให้ตรงกับเวอร์ชันจริงของงานที่จะแจกจ่าย

#define MyAppName "SmartGateLPR1"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SmartGateLPR1"
#define SourceDir "C:\Users\Gigabyte_2\source\repos\SmartGateLPR1\SmartGateLPR1\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{8F2B6C7A-3E1D-4F5A-9C6B-1A2B3C4D5E6F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; ต้องรันติดตั้งแบบ Admin เพราะเขียนลง Program Files
PrivilegesRequired=admin
OutputDir=output
OutputBaseFilename={#MyAppName}_Setup_v{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
; ปิดไว้ก่อน ถ้ามีไอคอนเป็น .ico ค่อยเปิดบรรทัดนี้แล้วใส่ path จริง
;SetupIconFile=SmartGateLPR.ico

[Languages]
Name: "thai"; MessagesFile: "compiler:Languages\Thai.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "สร้างไอคอนบนหน้าจอ (Desktop)"; GroupDescription: "ทางลัด:"

[Files]
; ตัวโปรแกรมหลัก (C#, self-contained)
Source: "{#SourceDir}\{#MyAppName}.exe"; DestDir: "{app}"; Flags: ignoreversion
; ทุกอย่างที่ .NET self-contained ต้องใช้ร่วมกับตัว exe (ถ้ามีไฟล์ .dll/.json อื่นหลุดออกมา)
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "ai\*"; Flags: ignoreversion recursesubdirs createallsubdirs
; ตัวเซิร์ฟเวอร์ AI (Python, PyInstaller) — ทั้งโฟลเดอร์ ai\ ที่ C# เรียกอัตโนมัติตอนเปิด
Source: "{#SourceDir}\ai\*"; DestDir: "{app}\ai"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppName}.exe"
Name: "{group}\ถอนการติดตั้ง {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppName}.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppName}.exe"; Description: "เปิดโปรแกรม {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; กันเศษไฟล์ค้าง เช่น log/ฐานข้อมูล local ที่โปรแกรมสร้างขึ้นเองตอนใช้งาน
Type: filesandordirs; Name: "{app}\ai\_internal\__pycache__"
