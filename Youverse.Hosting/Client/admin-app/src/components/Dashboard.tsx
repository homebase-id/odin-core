import React, {Component, useRef} from 'react';
import { Button, Image} from "react-bootstrap";
import {createDemoDataProvider} from "../provider/DemoDataProvider";
import {MediaProvider} from "../provider/MediaProvider";

function Dashboard(props: any) {

    const ddp = createDemoDataProvider();
    const fileRef = useRef();
    
    function addDemoData() {
        ddp.setPublicProfile().then(()=>
        {
            ddp.addConnectionRequests().then((r)=>
            {
                window.alert("Done");
            })
        });
        
    }

    async function handleTestUpload() {
        let mp = new MediaProvider();
        // if(fileRef.current)
        // {
        //     await mp.uploadImage(fileRef.current?.files[0]);
        // }
    }

    return (
        <div>
            {/* <Image src="https://images.gr-assets.com/hostedimages/1568664189ra/28157476.gif"/> */}
            <h1>Welcome</h1>
            <Button onClick={addDemoData}>Add Demo Data</Button>
            <br/>
            <br/>
            {/*<input type="file" ref={fileRef}/>  */}
            {/*<Button className="btn mt-10" onClick={handleTestUpload}>test upload</Button>*/}
        </div>
    );
}

export default Dashboard;