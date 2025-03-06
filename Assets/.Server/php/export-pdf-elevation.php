<?php

$password = $_POST["app_password"];
$id = $_POST["id"];
$jsonData = $_POST["data"];

if ($password != "qweasdv413240897fvhw") {
    echo "bad password ".$password;
    die();
}

file_put_contents('json/'.$id.'.json', $jsonData);
echo "success";

// clean up old files
$files = glob('json' . '/*');
$threshold = strtotime('-60 minutes');
  
foreach ($files as $file) {
    if (is_file($file)) {
        if ($threshold >= filemtime($file)) {
            unlink($file);
        }
    }
}
?>