function DirectoryWriteError(Dir: String): String;
var
  Missing: TArrayOfString;
  Current, Parent, TestFile: String;
  I, Suffix: Integer;
  Writable: Boolean;
begin
  Result := '';
  Dir := RemoveBackslashUnlessRoot(Dir);
  Current := Dir;
  SetArrayLength(Missing, 0);
  while (Current <> '') and not DirExists(Current) do begin
    I := GetArrayLength(Missing);
    SetArrayLength(Missing, I + 1);
    Missing[I] := Current;
    Parent := RemoveBackslashUnlessRoot(ExtractFileDir(Current));
    if Parent = Current then Break;
    Current := Parent;
  end;
  Writable := False;
  TestFile := '';
  try
    if ForceDirectories(Dir) then begin
      TestFile := AddBackslash(Dir) + '.aramate-write-test.tmp';
      Suffix := 0;
      while FileExists(TestFile) or DirExists(TestFile) do begin
        Suffix := Suffix + 1;
        TestFile := AddBackslash(Dir) + '.aramate-write-test-' + IntToStr(Suffix) + '.tmp';
      end;
      Writable := SaveStringToFile(TestFile, 'ARAMate directory write test', False);
    end;
  finally
    if TestFile <> '' then
      if FileExists(TestFile) then
        if not DeleteFile(TestFile) then Writable := False;
    { Missing 从目标目录排到父目录；RemoveDir 只删除空目录。 }
    for I := 0 to GetArrayLength(Missing) - 1 do
      if DirExists(Missing[I]) then
        if not RemoveDir(Missing[I]) then Writable := False;
  end;
  if not Writable then begin
    if IsAdminInstallMode then
      Result := '已使用管理员权限，仍无法在“' + Dir + '”中创建或写入文件，请换一个目录。'
    else
      Result := '无法在“' + Dir + '”中创建或写入文件（拒绝访问）。' + #13#10#13#10 +
        '运行时还要在安装目录中保存网站数据，因此需要一个当前用户可以写入的目录。请改用默认目录：' + #13#10 +
        ExpandConstant('{localappdata}\Programs\ARAMate') + #13#10 +
        '或其它你有写入权限的文件夹。磁盘根目录下的新文件夹（如 E:\ARAMate）在部分电脑上需要管理员权限。';
  end;
end;
