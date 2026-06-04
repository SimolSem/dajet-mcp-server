
-- Инструмент получает ссылку и наименование товара по его коду

DECLARE @input  string -- Входящий параметр (код товара)
PRIVATE @output array  -- Результат выполнения запроса

USE 'MS_TEST'

  SELECT TOP 1
         ref  = Ссылка
       , code = Код
       , name = Наименование
    INTO @output
    FROM Справочник.Номенклатура
   WHERE Код = @input

END

RETURN @output