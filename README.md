# PDF → Kindle Studio

Ứng dụng WPF native trên Windows, .NET 8, dùng để tái cấu trúc PDF thành ebook AZW3 có thể reflow trên Kindle.

## Pipeline

```text
PDF
  → PdfPig words/glyph + bounding box
  → line grouping
  → column reading order
  → paragraph / hyphen / Unicode normalization
  → header, footer, page number filtering
  → heading, chapter, table, code, footnote, image heuristics
  → BookDocument semantic model
  → XHTML + CSS
  → (Fixed Layout: render PNG từng trang + pre-paginated EPUB)
  → validated EPUB
  → Calibre ebook-convert
  → AZW3
```

Ứng dụng không nhét text thô vào AZW3 và cũng không rasterize toàn bộ PDF thành ảnh trong chế độ Smart Reflow.

## Yêu cầu

- Windows 10/11.
- .NET 8 Desktop Runtime hoặc .NET 8 SDK.
- Calibre nếu muốn tạo AZW3 cuối cùng. Ứng dụng không bundle `ebook-convert.exe`; có thể cài Calibre hoặc Browse tới `ebook-convert.exe` trong ứng dụng.

## Chạy từ source

```powershell
dotnet restore PdfToAzw3.sln
dotnet build PdfToAzw3.sln --configuration Debug
dotnet test PdfToAzw3.sln --configuration Debug
dotnet run --project src/PdfToAzw3.Desktop/PdfToAzw3.Desktop.csproj
```

## Cách dùng

1. Chọn PDF hoặc kéo thả PDF vào cửa sổ.
2. Kiểm tra title, author, language, description và cover.
3. Chọn profile và paragraph style.
4. Nếu PDF là scan, bật **OCR fallback cho trang scan**, chọn ngôn ngữ nếu cần rồi bấm **Analyze**. Windows OCR cần language pack tương ứng.
5. Bấm **Analyze** để đọc glyph, phân tích layout và xem quality score/warnings.
6. Bấm **Preview Ebook** để xem nội dung semantic đã phục hồi.
7. Bấm **Convert to AZW3**. EPUB trung gian được tạo cạnh PDF, validate trước khi Calibre chạy; AZW3 mặc định dùng tên PDF.

Profile **Kindle Auto** ưu tiên BookDocument semantic/reflowable. Profile **Fixed Layout** render từng trang PDF thành PNG, tạo XHTML có viewport đúng kích thước trang và đánh dấu EPUB là `pre-paginated`; vì vậy file có thể lớn hơn đáng kể.

Các tùy chọn header/footer/page number mặc định bật. Khi hủy, cancellation token được truyền xuyên pipeline và process Calibre do ứng dụng tạo sẽ được dừng theo process tree.

## Kiểm tra

Test hiện bao phủ:

- paragraph reflow và hyphen repair;
- Unicode tiếng Việt Form C;
- reading order hai cột;
- header/footer/page number lặp;
- heading/chapter và table heuristic;
- PdfPig đọc PDF thật;
- EPUB XML/ZIP, navigation, code, footnote, image và cover resource;
- OCR fallback qua abstraction và Fixed Layout rasterization từng trang.

## Log và output

- Log: `logs/app-yyyy-MM-dd.log` trong thư mục chạy ứng dụng.
- EPUB: cùng thư mục với PDF, dùng cùng tên và phần mở rộng `.epub`.
- AZW3: cùng thư mục với PDF, dùng cùng tên và phần mở rộng `.azw3`.

## Giới hạn có chủ ý

- OCR fallback đã tích hợp Windows OCR cho trang không có native text. Nếu thiếu language pack hoặc OCR engine không khả dụng, ứng dụng vẫn giữ cảnh báo theo trang và không tạo text giả.
- Heading, bảng, footnote, cột và header/footer dùng heuristic, nên warning cần được xem lại với PDF có layout bất thường.
- Profile **Fixed Layout** rasterize từng trang nên cần nhiều thời gian/dung lượng hơn; profile **Kindle Auto** vẫn ưu tiên ebook semantic/reflowable.
- Calibre là dependency bên ngoài và cần được cài riêng theo license của Calibre.
