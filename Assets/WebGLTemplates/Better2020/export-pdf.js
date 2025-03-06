
const { jsPDF } = window.jspdf;

function getPdfJson(callback) {
    var searchParams = new URLSearchParams(window.location.search);
    var id = searchParams.get('id');
    var url = "https://www.splensoft.com/ors/php/json/" + id + ".json";
    var xhr = new XMLHttpRequest();
    xhr.open('GET', url, true);
    xhr.responseType = 'json';
    xhr.onload = function() {
      var status = xhr.status;
      if (status === 200) {
        callback(null, xhr.response);
      } else {
        callback(status, xhr.response);
      }
    };
    xhr.send();
}

function exportPdf(data) {

    //var data = JSON.parse(json);
    const pageWidthPx = 1191;
    const pageHeightPx = 842;
    const doc = new jsPDF({
        unit: 'px',
        orientation: 'landscape',
        format: [pageWidthPx, pageHeightPx]
    });

    var image1Data = doc.getImageProperties(data.image1);
    var image2Data = doc.getImageProperties(data.image2);

    //console.log(image1Data);

    var maxImageHeight = pageHeightPx * 0.4;
    var startingPos = (pageWidthPx * 0.25) - 40;
    doc.setFillColor(0, 0, 255); // blue fill
    doc.rect(20, 20, pageWidthPx - 350, 50, "F");
    doc.setTextColor(255);
    doc.setFontSize(36);
    doc.text(data.title, 30, 50);
    doc.setFontSize(12);
    doc.text(data.subtitle, 40, 65);
    var headerHeight = 90;
    printImage(doc, data.image1, startingPos, headerHeight, image1Data.width, image1Data.height, maxImageHeight, pageWidthPx * 0.23, "image1", actualPrintedWidth => startingPos += actualPrintedWidth + 30);
    printImage(doc, data.image2, startingPos, headerHeight, image2Data.width, image2Data.height, maxImageHeight, pageWidthPx * 0.23, "image2");
    getBase64Image("floor_finish.png", floorFinishImageData => {
        //var floorFinishImageProperties = doc.getImageProperties(floorFinishImageData);
        doc.addImage(floorFinishImageData, 'png', (pageWidthPx * 0.25) -40, maxImageHeight + 90, pageWidthPx * 0.49, 20, "floor_finish", "none", 0);

        for (const element of data.assemblies) {
            console.log(element);
        }

        var cursorPosition = 90;
        
        for (let i = 0; i < data.assemblies.length; i++) {
          const assembly = data.assemblies[i];
          
          doc.setTextColor(0);
          //console.log(assembly);

          doc.setFontSize(12);
          var cursorPosMod = i > 0 ? -5 : 0;
          doc.text(String(assembly.title), 24, cursorPosition + cursorPosMod);
          cursorPosition += 5;

          doc.setFontSize(10);

          var margin = {
              left: 20,
              right: 15,
              top: cursorPosition,
              bottom: 20,
            };
  
            // space between each section
          const spacing = 5;
          const sections = 5;
  
          const printWidth = doc.internal.pageSize.width- (margin.left + margin.right);
          const sectionWidth = (printWidth - ((sections - 1) * spacing)) / sections;

          var tableHeight = 0;
          doc.autoTable({
            //head: assembly.title,
            body: assembly.selectableData,
            tableWidth: sectionWidth,
            tableLineColor: [0, 0, 0],
            tableLineWidth: 0.75,
            theme: 'grid',
            styles: {textColor: [0, 0, 0]},
            bodyStyles: {lineColor: [0, 0, 0], lineWidth: 0.75},
            margin,
            //cellWidth: 'auto',
            rowPageBreak: 'avoid', // avoid breaking rows into multiple sections
            didDrawPage({table, pageNumber}) {
              const docPage = doc.internal.getNumberOfPages();
              const nextShouldWrap = pageNumber % sections;
              console.log(table.finalY);
              //cursorPosition = table.finalY;
              if (nextShouldWrap) {
                
                // move to previous page, so when autoTable calls
                // addPage() it will still be the same current page
                doc.setPage(docPage - 1);
        
                var sectionWidth = 0;
                
                table.columns.forEach((element) => sectionWidth += element.width);

                // change left margin which will controll x position
                table.settings.margin.top = maxImageHeight + 130;
                table.settings.margin.left += sectionWidth + spacing;

              } else {
                
                // reset left margin for the first section in every page
                table.settings.margin.left = margin.left;
              }
            },
            didParseCell: function (HookData) {
              // console.log(HookData);
              // cursorPosition = HookData.table.finalY
            }
          });

          cursorPosition = doc.lastAutoTable.finalY + 15;

          // if (!isNaN(tableHeight)) {
          //   cursorPosition += tableHeight;
          // }
        }

        getBase64Image("logo.png", logoImageData => {
            var lowerRightAreaWidth = 200;
            var lowerRightAreaX = pageWidthPx - 520;
            var lowerRightAreaYStart = pageHeightPx - 400;
            var lowerRightAreaCurrentY = lowerRightAreaYStart + 70;
            var textXStart = lowerRightAreaX + 5;
            var textCurrentYOffset = 9;
            var rectHeight = 12;

            doc.addImage(logoImageData, 'png', lowerRightAreaX, lowerRightAreaYStart, lowerRightAreaWidth, 70, "logo", "none", 0);
            doc.rect(lowerRightAreaX, lowerRightAreaCurrentY, lowerRightAreaWidth, rectHeight);
            doc.text("Account Name: " + String(data.AccountName), textXStart, lowerRightAreaCurrentY + textCurrentYOffset);

            lowerRightAreaCurrentY += rectHeight;
            doc.rect(lowerRightAreaX, lowerRightAreaCurrentY, lowerRightAreaWidth, rectHeight);
            doc.text("Account Adress: " + String(data.AccountAddressLine1), textXStart, lowerRightAreaCurrentY + textCurrentYOffset);

            lowerRightAreaCurrentY += rectHeight;
            doc.rect(lowerRightAreaX, lowerRightAreaCurrentY, lowerRightAreaWidth, rectHeight);
            doc.text("                        " + String(data.AccountAddressLine2), textXStart, lowerRightAreaCurrentY + textCurrentYOffset);

            lowerRightAreaCurrentY += rectHeight;
            doc.rect(lowerRightAreaX, lowerRightAreaCurrentY, lowerRightAreaWidth, rectHeight);
            doc.text("Project Name: " + String(data.ProjectName), textXStart, lowerRightAreaCurrentY + textCurrentYOffset);

            lowerRightAreaCurrentY += rectHeight;
            doc.rect(lowerRightAreaX, lowerRightAreaCurrentY, lowerRightAreaWidth, rectHeight);
            doc.text("Project #: " + String(data.ProjectNumber), textXStart, lowerRightAreaCurrentY + textCurrentYOffset);

            lowerRightAreaCurrentY += rectHeight;
            doc.rect(lowerRightAreaX, lowerRightAreaCurrentY, lowerRightAreaWidth, rectHeight);
            doc.text("Order Reference #: " + String(data.OrderReferenceNumber), textXStart, lowerRightAreaCurrentY + textCurrentYOffset);

            lowerRightAreaCurrentY += rectHeight;
            var sigBoxWidth = 300;
            var sigLineMargin = 10;
            var sigBoxStart = lowerRightAreaYStart + 70;
            doc.rect(lowerRightAreaX - sigBoxWidth, sigBoxStart, sigBoxWidth, lowerRightAreaCurrentY - sigBoxStart);
            doc.text("Customer Acceptance and Configuration Acknowledgement", lowerRightAreaX - sigBoxWidth + 5, sigBoxStart + textCurrentYOffset);

            // sig line
            doc.rect(lowerRightAreaX - sigBoxWidth + sigLineMargin, lowerRightAreaCurrentY - 13, sigBoxWidth - (sigLineMargin * 2), 1);

            doc.text("Signature", lowerRightAreaX - sigBoxWidth + sigLineMargin, lowerRightAreaCurrentY - 5);
            doc.text("Date", lowerRightAreaX - 125, lowerRightAreaCurrentY - 5);
            
            // lowerRightAreaCurrentY += rectHeight;
            // doc.rect(lowerRightAreaX, lowerRightAreaCurrentY, lowerRightAreaWidth, rectHeight);
            // //doc.text("Quote Reference #: 1275", textXStart, lowerRightAreaCurrentY + textCurrentYOffset);

            doc.save("a3.pdf");
        });
        
    });
}

function printImage(jsPdf, imageData, x, y, imageWidth, imageHeight, maxImageHeight, printedWidth, id, callback) {
    var printedHeight = (printedWidth / imageWidth) * imageHeight;
    if (printedHeight > maxImageHeight) {
        var dif = maxImageHeight / printedHeight;
        printedWidth *= dif;
        printedHeight *= dif;
    }

    // if (printedWidth > maxImageWidth)
    // {
    //   var dif = maxImageWidth / printedWidth;
    //   printedWidth *= dif;
    //   printedHeight *= dif;
    // }

    var addToY = 0;
    if (printedHeight < maxImageHeight) {
      addToY = maxImageHeight - printedHeight;
    }

    console.log("Adding image data to pdf for " + id);
    jsPdf.addImage(imageData, 'png', x, y + addToY, printedWidth, printedHeight, id, "none", 0);
    if (callback != null) {
        callback(printedWidth);
    }
}

function getBase64Image(imgUrl, callback) {

    var img = new Image();

    // onload fires when the image is fully loadded, and has width and height

    img.onload = function(){

      var canvas = document.createElement("canvas");
      canvas.width = img.width;
      canvas.height = img.height;
      var ctx = canvas.getContext("2d");
      ctx.drawImage(img, 0, 0);
      var dataURL = canvas.toDataURL("image/png"),
          dataURL = dataURL.replace(/^data:image\/(png|jpg);base64,/, "");

      callback(dataURL); // the base64 string

    };

    // set attributes and src 
    img.setAttribute('crossOrigin', 'anonymous'); //
    img.src = imgUrl;

}

getPdfJson((status, json) => {
    console.log(status);
    console.log(json);
    exportPdf(json);
});