window.onload = function(){
    var a = document.createElement("a");
    const urlParams = new URLSearchParams(window.location.search);
    const fileName = urlParams.get('id');
    const link = `https://www.splensoft.com/ors/php/obj/${fileName}.zip`;
    a.href = link;
    a.download = "ors-exported-obj";
    a.click();
  };