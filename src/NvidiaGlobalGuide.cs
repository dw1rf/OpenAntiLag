using System;
namespace OpenAntiLag {
    public static class NvidiaGlobalGuide {
        public static string Get(bool smooth) {
            return "Open AntiLag 0.5.3 • NVIDIA • глобальные настройки\r\nПроверено 10.10.2026: RTX 4070 Ti / 617.42 (чтение и временная сессия без сохранения).\r\n\r\n"+
                "Во вкладке NVIDIA выберите режим и нажмите «Применить NVIDIA». Исходные изменяемые значения будут сохранены; «Восстановить NVIDIA» возвращает их. Для другого набора сначала выполните восстановление. Закройте игры перед применением.\r\n\r\n"+
                NvidiaPreset.Description(smooth,141,false,false)+
                "\r\nЭтот TXT описывает основной набор с выключенными дополнительными флажками; лимит 141 FPS в режиме G-SYNC приведён как пример для 144 Гц. В приложении укажите свой лимит.\r\n"+
                "\r\nОграничения: аппаратная проверка постоянного сохранения не выполнялась. Некоторые интерфейсы Low Latency недокументированы; при несовместимости приложение сообщает ошибку и сохраняет копию для восстановления. Новые версии драйвера требуют повторной проверки.\r\n"+
                "\r\nИсточники:\r\nhttps://github.com/NVIDIA/nvapi\r\nhttps://github.com/Orbmu2k/nvidiaProfileInspector\r\nhttps://www.nvidia.com/en-gb/geforce/guides/system-latency-optimization-guide/\r\nhttps://github.com/valleyofdoom/PC-Tuning/blob/main/docs/configure-nvidia.md\r\n";
        }
    }
}
