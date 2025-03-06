<?php

$password = $_POST["app_password"];
$id = $_POST["id"];

$objData = null;
$mtlData = null;
$data = null;

if ($password != "qweasdv413240897fvhw") {
    echo "bad password ".$password;
    die();
}

$zip = new ZipArchive;

if(isset($_POST['json']))
{
    $data = json_decode($_POST['json'], true);
    $objData = $data["ObjString"];
    $mtlData = $data["MtlString"];
}
else
{
    // Backwards compatibility
    $objData = $_POST["objData"];
    $mtlData = $_POST["mtlData"];
}

if ($zip->open('obj/'.$id.'.zip', ZipArchive::CREATE) === TRUE)
{
    // Add a file new.txt file to zip using the text specified
    $zip->addFromString($id.'.obj', $objData);
    $zip->addFromString($id.'.mtl', $mtlData);

    if ($data != null)
    {
        foreach ($data["Textures"] as &$value) {
            $zip->addFromString($value["Name"].".png", base64_decode($value["TextureBase64"]));
        }
    }
    
    // All files are added, so close the zip file.
    $zip->close();
}

echo "success";

// clean up old files
$files = glob('obj' . '/*');
$threshold = strtotime('-60 minutes');
  
foreach ($files as $file) {
    if (is_file($file)) {
        if ($threshold >= filemtime($file)) {
            unlink($file);
        }
    }
}
?>