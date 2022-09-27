"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[634,476],{6691:function(e,t,n){var r=n(4165),a=n(5861),i=n(9439),s=n(2791),l=n(4164),o=n(4990),c=n(9779),u=n(3412),d=n(9072),m=n(184);t.Z=function(e){var t=e.title,n=e.confirmText,f=e.isOpen,h=e.acl,v=e.targetDrive,g=e.onConfirm,x=e.onCancel,p=(0,u.Z)("modal-container"),b=(0,c.Z)().save,j=b.mutate,C=b.status,w=(0,s.useState)(),y=(0,i.Z)(w,2),Z=y[0],N=y[1];if(!f)return null;var k=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(){var t;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.t0=Uint8Array,e.next=3,Z.arrayBuffer();case 3:e.t1=e.sent,t=new e.t0(e.t1),j({acl:h,bytes:t,fileId:void 0,targetDrive:v},{onSuccess:g});case 6:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}(),I=(0,m.jsxs)("div",{className:"relative z-50","aria-labelledby":"modal-title",role:"dialog","aria-modal":"true",children:[(0,m.jsx)("div",{className:"fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"}),(0,m.jsx)("div",{className:"fixed inset-0 z-10 overflow-y-auto",children:(0,m.jsx)("div",{className:"flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0",children:(0,m.jsxs)("div",{className:"relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all dark:bg-black sm:my-8 sm:w-full sm:max-w-lg",children:[(0,m.jsx)("div",{className:"bg-white px-4 pt-5 pb-4 dark:bg-black sm:p-6 sm:pb-4",children:(0,m.jsx)("div",{className:"sm:flex sm:items-start",children:(0,m.jsxs)("div",{className:"mt-3 text-center sm:mt-0 sm:ml-4 sm:text-left",children:[(0,m.jsx)("h3",{className:"mb-3 text-lg font-medium leading-6 text-gray-900 dark:text-slate-50",id:"modal-title",children:t}),(0,m.jsx)("input",{onChange:function(e){var t=e.target.files[0];t&&N(t)},type:"file",accept:"image/png, image/jpeg, image/tiff, image/webp",className:"w-full rounded border border-gray-300 bg-white py-1 px-3 text-base leading-8 text-gray-700 outline-none transition-colors duration-200 ease-in-out focus:border-indigo-500 focus:ring-2 focus:ring-indigo-200 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-100"})]})})}),(0,m.jsxs)("div",{className:"-m-2 flex flex-row-reverse px-4 py-3",children:[(0,m.jsx)(d.Z,{className:"m-2",onClick:k,state:C,children:null!==n&&void 0!==n?n:"Add"}),(0,m.jsx)(d.Z,{className:"m-2",type:"secondary",onClick:x,children:(0,o.t)("Cancel")})]})]})})})]});return(0,l.createPortal)(I,p)}},3197:function(e,t,n){var r=n(9439),a=n(2791),i=n(4990),s=n(9779),l=n(1803),o=n(1088),c=n(7804),u=n(7134),d=n(6691),m=n(184);t.Z=function(e){var t=e.targetDrive,n=e.acl,f=e.onChange,h=e.defaultValue,v=e.name,g=(0,s.Z)("string"===typeof h?h:void 0,t),x=g.fetch,p=x.data,b=x.isLoading,j=g.remove.mutate,C=(0,a.useState)(!1),w=(0,r.Z)(C,2),y=w[0],Z=w[1],N=(0,a.useState)(!1),k=(0,r.Z)(N,2),I=k[0],M=k[1];return b?(0,m.jsx)("div",{className:"aspect-square max-w-[20rem] animate-pulse bg-slate-100 dark:bg-slate-700"}):(0,m.jsxs)(m.Fragment,{children:[p?(0,m.jsx)("div",{className:"flex",children:(0,m.jsxs)("div",{className:"relative mr-auto",children:[(0,m.jsx)("button",{className:"absolute top-2 right-2 rounded-full bg-white p-2",onClick:function(e){return e.preventDefault(),Z(!0),!1},children:(0,m.jsx)(c.Z,{className:"h-4 w-4 text-black"})}),(0,m.jsx)("button",{className:"absolute bottom-2 right-2 rounded-full bg-red-200 p-2",onClick:function(){return M(!0),!1},children:(0,m.jsx)(u.Z,{className:"h-4 w-4 text-black"})}),(0,m.jsx)("img",{src:p,alt:p,className:"max-h-[20rem]",onClick:function(){Z(!0)}})]})}):(0,m.jsxs)("div",{className:"relative flex aspect-video max-w-[20rem] cursor-pointer bg-slate-100 dark:bg-slate-700",onClick:function(e){e.preventDefault(),Z(!0)},children:[(0,m.jsx)(o.Z,{className:"m-auto h-8 w-8"}),(0,m.jsx)("p",{className:"absolute inset-0 top-auto pb-5 text-center text-slate-400",children:(0,i.t)("No image selected")})]}),(0,m.jsx)(d.Z,{acl:n,isOpen:y,targetDrive:t,title:(0,i.t)("Insert image"),confirmText:(0,i.t)("Add"),onCancel:function(){return Z(!1)},onConfirm:function(e){f({target:{name:v,value:e}}),Z(!1)}}),(0,m.jsx)(l.Z,{title:"Remove Current Image",confirmText:"Permanently remove",needConfirmation:I,onConfirm:function(){j({fileId:"string"===typeof h?h:void 0,targetDrive:t},{onSuccess:function(){f({target:{name:v,value:""}})}})},onCancel:function(){M(!1)},children:(0,m.jsx)("p",{className:"text-sm text-gray-500",children:(0,i.t)("Are you sure you want to remove the current file? This action cannot be undone.")})})]})}},6916:function(e,t,n){var r=n(1413),a=n(184);t.Z=function(e){var t;return(0,a.jsx)("input",(0,r.Z)((0,r.Z)({},e),{},{type:null!==(t=e.type)&&void 0!==t?t:"input",className:"w-full rounded border border-gray-300 bg-white py-1 px-3 text-base leading-8 text-gray-700 outline-none transition-colors duration-200 ease-in-out focus:border-indigo-500 focus:ring-2 focus:ring-indigo-300 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-100 ".concat(e.className)}))}},2465:function(e,t,n){var r=n(1413),a=n(184);t.Z=function(e){return(0,a.jsx)("textarea",(0,r.Z)((0,r.Z)({},e),{},{className:"w-full rounded border border-gray-300 bg-white py-1 px-3 text-base leading-8 text-gray-700 outline-none transition-colors duration-200 ease-in-out focus:border-indigo-500 focus:ring-2 focus:ring-indigo-200 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-100 ".concat(e.className)}))}},7673:function(e,t,n){n.r(t),n.d(t,{default:function(){return he}});var r,a=n(9439),i=n(2791),s=n(6871),l=n(3990),o=n(4990),c=n(1816),u=n(777),d=n(1413),m=n(6752),f=n(2878),h=n(3197),v=n(6916),g=n(2465),x=n(366),p=n(2100),b=n(6283),j=n(5473),C=n(4925),w=n(184),y=["className","active","reversed"],Z=["className"],N=function(e){var t=e.className,n=e.active,r=e.reversed,a=(0,C.Z)(e,y);return(0,w.jsx)("button",(0,d.Z)((0,d.Z)({},a),{},{className:"".concat(null!==t&&void 0!==t?t:""," cursor-pointer px-1 py-2 ").concat(r?n?"text-white":"text-slate-400":n?"text-black dark:text-white":"text-slate-200 dark:text-slate-700")}))},k=function(e){var t=e.className,n=(0,C.Z)(e,Z);return(0,w.jsx)("div",(0,d.Z)((0,d.Z)({},n),{},{className:"".concat(null!==t&&void 0!==t?t:""," relative -mx-1 mb-3 flex flex-row flex-wrap border-b border-slate-200 border-opacity-50")}))},I=function(e){var t=e.className;return(0,w.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 384 512",fill:"currentColor",className:t,children:(0,w.jsx)("path",{d:"M321.1 242.4C340.1 220.1 352 191.6 352 160c0-70.59-57.42-128-128-128L32 32.01c-17.67 0-32 14.31-32 32s14.33 32 32 32h16v320H32c-17.67 0-32 14.31-32 32s14.33 32 32 32h224c70.58 0 128-57.41 128-128C384 305.3 358.6 264.8 321.1 242.4zM112 96.01H224c35.3 0 64 28.72 64 64s-28.7 64-64 64H112V96.01zM256 416H112v-128H256c35.3 0 64 28.71 64 63.1S291.3 416 256 416z"})})},M=function(e){var t=e.className;return(0,w.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 384 512",fill:"currentColor",className:t,children:(0,w.jsx)("path",{d:"M384 64.01c0 17.69-14.31 32-32 32h-58.67l-133.3 320H224c17.69 0 32 14.31 32 32s-14.31 32-32 32H32c-17.69 0-32-14.31-32-32s14.31-32 32-32h58.67l133.3-320H160c-17.69 0-32-14.31-32-32s14.31-32 32-32h192C369.7 32.01 384 46.33 384 64.01z"})})},S=function(e){var t=e.className;return(0,w.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 448 512",fill:"currentColor",className:t,children:(0,w.jsx)("path",{d:"M416 448H32c-17.69 0-32 14.31-32 32s14.31 32 32 32h384c17.69 0 32-14.31 32-32S433.7 448 416 448zM48 64.01H64v160c0 88.22 71.78 159.1 160 159.1s160-71.78 160-159.1v-160h16c17.69 0 32-14.32 32-32s-14.31-31.1-32-31.1l-96-.0049c-17.69 0-32 14.32-32 32s14.31 32 32 32H320v160c0 52.94-43.06 95.1-96 95.1S128 276.1 128 224v-160h16c17.69 0 32-14.31 32-32s-14.31-32-32-32l-96 .0049c-17.69 0-32 14.31-32 31.1S30.31 64.01 48 64.01z"})})},H=function(e){var t=e.className;return(0,w.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 448 512",fill:"currentColor",className:t,children:(0,w.jsx)("path",{d:"M96 224C84.72 224 74.05 226.3 64 229.9V224c0-35.3 28.7-64 64-64c17.67 0 32-14.33 32-32S145.7 96 128 96C57.42 96 0 153.4 0 224v96c0 53.02 42.98 96 96 96s96-42.98 96-96S149 224 96 224zM352 224c-11.28 0-21.95 2.305-32 5.879V224c0-35.3 28.7-64 64-64c17.67 0 32-14.33 32-32s-14.33-32-32-32c-70.58 0-128 57.42-128 128v96c0 53.02 42.98 96 96 96s96-42.98 96-96S405 224 352 224z"})})},D=function(e){var t=e.className;return(0,w.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 640 512",fill:"currentColor",className:t,children:(0,w.jsx)("path",{d:"M414.8 40.79L286.8 488.8C281.9 505.8 264.2 515.6 247.2 510.8C230.2 505.9 220.4 488.2 225.2 471.2L353.2 23.21C358.1 6.216 375.8-3.624 392.8 1.232C409.8 6.087 419.6 23.8 414.8 40.79H414.8zM518.6 121.4L630.6 233.4C643.1 245.9 643.1 266.1 630.6 278.6L518.6 390.6C506.1 403.1 485.9 403.1 473.4 390.6C460.9 378.1 460.9 357.9 473.4 345.4L562.7 256L473.4 166.6C460.9 154.1 460.9 133.9 473.4 121.4C485.9 108.9 506.1 108.9 518.6 121.4V121.4zM166.6 166.6L77.25 256L166.6 345.4C179.1 357.9 179.1 378.1 166.6 390.6C154.1 403.1 133.9 403.1 121.4 390.6L9.372 278.6C-3.124 266.1-3.124 245.9 9.372 233.4L121.4 121.4C133.9 108.9 154.1 108.9 166.6 121.4C179.1 133.9 179.1 154.1 166.6 166.6V166.6z"})})},F=function(e){var t=e.className;return(0,w.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 448 512",fill:"currentColor",className:t,children:(0,w.jsx)("path",{d:"M448 448c0 17.69-14.33 32-32 32h-96c-17.67 0-32-14.31-32-32s14.33-32 32-32h16v-144h-224v144H128c17.67 0 32 14.31 32 32s-14.33 32-32 32H32c-17.67 0-32-14.31-32-32s14.33-32 32-32h16v-320H32c-17.67 0-32-14.31-32-32s14.33-32 32-32h96c17.67 0 32 14.31 32 32s-14.33 32-32 32H112v112h224v-112H320c-17.67 0-32-14.31-32-32s14.33-32 32-32h96c17.67 0 32 14.31 32 32s-14.33 32-32 32h-16v320H416C433.7 416 448 430.3 448 448z"})})},z=function(e){var t=e.className;return(0,w.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 576 512",fill:"currentColor",className:t,children:(0,w.jsx)("path",{d:"M55.1 56.04C55.1 42.78 66.74 32.04 79.1 32.04H111.1C125.3 32.04 135.1 42.78 135.1 56.04V176H151.1C165.3 176 175.1 186.8 175.1 200C175.1 213.3 165.3 224 151.1 224H71.1C58.74 224 47.1 213.3 47.1 200C47.1 186.8 58.74 176 71.1 176H87.1V80.04H79.1C66.74 80.04 55.1 69.29 55.1 56.04V56.04zM118.7 341.2C112.1 333.8 100.4 334.3 94.65 342.4L83.53 357.9C75.83 368.7 60.84 371.2 50.05 363.5C39.26 355.8 36.77 340.8 44.47 330.1L55.59 314.5C79.33 281.2 127.9 278.8 154.8 309.6C176.1 333.1 175.6 370.5 153.7 394.3L118.8 432H152C165.3 432 176 442.7 176 456C176 469.3 165.3 480 152 480H64C54.47 480 45.84 474.4 42.02 465.6C38.19 456.9 39.9 446.7 46.36 439.7L118.4 361.7C123.7 355.9 123.8 347.1 118.7 341.2L118.7 341.2zM512 64C529.7 64 544 78.33 544 96C544 113.7 529.7 128 512 128H256C238.3 128 224 113.7 224 96C224 78.33 238.3 64 256 64H512zM512 224C529.7 224 544 238.3 544 256C544 273.7 529.7 288 512 288H256C238.3 288 224 273.7 224 256C224 238.3 238.3 224 256 224H512zM512 384C529.7 384 544 398.3 544 416C544 433.7 529.7 448 512 448H256C238.3 448 224 433.7 224 416C224 398.3 238.3 384 256 384H512z"})})},L=function(e){var t=e.className;return(0,w.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 512 512",fill:"currentColor",className:t,children:(0,w.jsx)("path",{d:"M16 96C16 69.49 37.49 48 64 48C90.51 48 112 69.49 112 96C112 122.5 90.51 144 64 144C37.49 144 16 122.5 16 96zM480 64C497.7 64 512 78.33 512 96C512 113.7 497.7 128 480 128H192C174.3 128 160 113.7 160 96C160 78.33 174.3 64 192 64H480zM480 224C497.7 224 512 238.3 512 256C512 273.7 497.7 288 480 288H192C174.3 288 160 273.7 160 256C160 238.3 174.3 224 192 224H480zM480 384C497.7 384 512 398.3 512 416C512 433.7 497.7 448 480 448H192C174.3 448 160 433.7 160 416C160 398.3 174.3 384 192 384H480zM16 416C16 389.5 37.49 368 64 368C90.51 368 112 389.5 112 416C112 442.5 90.51 464 64 464C37.49 464 16 442.5 16 416zM112 256C112 282.5 90.51 304 64 304C37.49 304 16 282.5 16 256C16 229.5 37.49 208 64 208C90.51 208 112 229.5 112 256z"})})},T=n(6691),B=function(e){var t=e.className;return(0,w.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 512 512",fill:"currentColor",className:t,children:(0,w.jsx)("path",{d:"M447.1 32h-384C28.64 32-.0091 60.65-.0091 96v320c0 35.35 28.65 64 63.1 64h384c35.35 0 64-28.65 64-64V96C511.1 60.65 483.3 32 447.1 32zM111.1 96c26.51 0 48 21.49 48 48S138.5 192 111.1 192s-48-21.49-48-48S85.48 96 111.1 96zM446.1 407.6C443.3 412.8 437.9 416 432 416H82.01c-6.021 0-11.53-3.379-14.26-8.75c-2.73-5.367-2.215-11.81 1.334-16.68l70-96C142.1 290.4 146.9 288 152 288s9.916 2.441 12.93 6.574l32.46 44.51l93.3-139.1C293.7 194.7 298.7 192 304 192s10.35 2.672 13.31 7.125l128 192C448.6 396 448.9 402.3 446.1 407.6z"})})},V={alias:m.Ib.BlogMainContentDriveId.toString(),type:m.Hm.DriveType.toString()},A=function(){var e=(0,i.useState)(!1),t=(0,a.Z)(e,2),n=t[0],r=t[1],s=(0,p._7)();return(0,w.jsxs)(w.Fragment,{children:[(0,w.jsx)(N,{onClick:function(e){e.preventDefault(),r(!0)},children:(0,w.jsx)(B,{className:"h-5 w-5"})}),(0,w.jsx)(T.Z,{isOpen:n,onCancel:function(){r(!1)},onConfirm:function(e){!function(e,t){var n={type:"image",imageFileId:t,children:[{text:""}]};b.YR.insertNodes(e,n)}(s,e),r(!1)},title:(0,o.t)("Insert image"),confirmText:(0,o.t)("Add"),acl:{requiredSecurityGroup:m.hh.Anonymous},targetDrive:V})]})},O=new WeakMap,P=function(e){e.forEach((function(e){if(O.has(e.target)){var t=O.get(e.target);(e.isIntersecting||e.intersectionRatio>0)&&(r.unobserve(e.target),O.delete(e.target),t())}}))},_=function(){return void 0===r&&(r=new IntersectionObserver(P,{rootMargin:"100px",threshold:.15})),r},E=n(9779),R=function(e){var t,n,r=e.targetDrive,s=e.fileId,l=e.className,o=e.alt,c=e.title,u=(0,i.useState)(!1),d=(0,a.Z)(u,2),m=d[0],f=d[1],h=(0,i.useRef)(null),v=(0,E.Z)(m?s:void 0,r).fetch.data;return t=h,n=function(){f(!0)},(0,i.useEffect)((function(){var e=t.current,r=_();if(e)return O.set(e,n),r.observe(e),function(){O.delete(e),r.unobserve(e)}}),[]),(0,w.jsx)("img",{src:v,alt:m&&v?o:" ",className:"".concat(l," ").concat((!m||!v)&&"h-full w-full animate-pulse bg-slate-100"),title:c,ref:h})},U={"mod+b":"bold","mod+i":"italic","mod+u":"underline","mod+`":"code"},q=["numbered-list","bulleted-list"],Q={alias:m.Ib.BlogMainContentDriveId.toString(),type:m.Hm.DriveType.toString()},W=function(e,t){K(e,t)?b.ML.removeMark(e,t):b.ML.addMark(e,t,!0)},G=function(e,t){var n=arguments.length>2&&void 0!==arguments[2]?arguments[2]:"type",r=e.selection;if(!r)return!1;var i=Array.from(b.ML.nodes(e,{at:b.ML.unhangRange(e,r),match:function(e){return!b.ML.isEditor(e)&&b.W_.isElement(e)&&e[n]===t}})),s=(0,a.Z)(i,1),l=s[0];return!!l},K=function(e,t){var n=b.ML.marks(e);return!!n&&!0===n[t]},Y=function(e){var t=e.attributes,n=e.children,r=e.element,a={};switch(r.type){case"block-quote":return(0,w.jsx)("blockquote",(0,d.Z)((0,d.Z)({style:a},t),{},{className:"border-l-4 pl-2",children:n}));case"bulleted-list":return(0,w.jsx)("ul",(0,d.Z)((0,d.Z)({style:a},t),{},{className:"list-disc pl-5",children:n}));case"heading-one":return(0,w.jsx)("h1",(0,d.Z)((0,d.Z)({style:a},t),{},{className:"text-2xl",children:n}));case"heading-two":return(0,w.jsx)("h2",(0,d.Z)((0,d.Z)({style:a},t),{},{className:"text-xl",children:n}));case"list-item":return(0,w.jsx)("li",(0,d.Z)((0,d.Z)({style:a},t),{},{children:n}));case"numbered-list":return(0,w.jsx)("ol",(0,d.Z)((0,d.Z)({style:a},t),{},{className:"list-decimal pl-5",children:n}));case"image":var i=(0,p.vt)(),s=(0,p.UE)();return(0,w.jsxs)("div",(0,d.Z)((0,d.Z)({},t),{},{children:[(0,w.jsx)("div",{contentEditable:!1,className:"pl-2",children:(0,w.jsx)(R,{targetDrive:Q,fileId:r.imageFileId,className:"max-w-md ".concat(i&&s?"outline outline-4 outline-offset-2 outline-indigo-500":"")})}),n]}));default:return(0,w.jsx)("p",(0,d.Z)((0,d.Z)({style:a},t),{},{children:n}))}},J=function(e){var t=e.attributes,n=e.children,r=e.leaf;return r.bold&&(n=(0,w.jsx)("strong",{children:n})),r.code&&(n=(0,w.jsx)("code",{children:n})),r.italic&&(n=(0,w.jsx)("em",{children:n})),r.underline&&(n=(0,w.jsx)("u",{children:n})),(0,w.jsx)("span",(0,d.Z)((0,d.Z)({},t),{},{children:n}))},X=function(e){var t=e.format,n=e.icon,r=(0,p.ui)();return(0,w.jsx)(N,{active:G(r,t,"type"),onMouseDown:function(e){e.preventDefault(),function(e,t){var n=G(e,t,"type"),r=q.includes(t);b.YR.unwrapNodes(e,{match:function(e){return!b.ML.isEditor(e)&&b.W_.isElement(e)&&q.includes(e.type)},split:!0});var a={type:n?"paragraph":r?"list-item":t};if(b.YR.setNodes(e,a),!n&&r){var i={type:t,children:[]};b.YR.wrapNodes(e,i)}}(r,t)},children:n&&(0,w.jsx)(n,{className:"h-5 w-5"})})},$=function(e){var t=e.format,n=e.icon,r=(0,p.ui)();return(0,w.jsx)(N,{active:K(r,t),onMouseDown:function(e){e.preventDefault(),W(r,t)},children:n&&(0,w.jsx)(n,{className:"h-5 w-5"})})},ee=function(e){var t=e.defaultValue,n=e.placeholder,r=e.onChange,a=(0,i.useCallback)((function(e){return(0,w.jsx)(Y,(0,d.Z)({},e))}),[]),s=(0,i.useCallback)((function(e){return(0,w.jsx)(J,(0,d.Z)({},e))}),[]),l=(0,i.useMemo)((function(){return(0,j.VC)((0,p.BU)((0,b.Jh)()))}),[]),o=(0,i.useMemo)((function(){return(0,f.Z)(r,1500)}),[r]);return(0,w.jsx)("section",{className:"w-[100%] overflow-hidden",children:(0,w.jsxs)(p.mH,{editor:l,value:t,onChange:o,children:[(0,w.jsxs)(k,{children:[(0,w.jsx)($,{format:"bold",icon:I}),(0,w.jsx)($,{format:"italic",icon:M}),(0,w.jsx)($,{format:"underline",icon:S}),(0,w.jsx)($,{format:"code",icon:D}),(0,w.jsx)(X,{format:"heading-one",icon:F}),(0,w.jsx)(X,{format:"heading-two",icon:F}),(0,w.jsx)(X,{format:"block-quote",icon:H}),(0,w.jsx)(X,{format:"numbered-list",icon:z}),(0,w.jsx)(X,{format:"bulleted-list",icon:L}),(0,w.jsx)(A,{})]}),(0,w.jsx)(p.CX,{renderElement:a,renderLeaf:s,placeholder:n,spellCheck:!0,autoFocus:!0,onKeyDown:function(e){for(var t in U){if((0,x.ZP)(t,e))e.preventDefault(),W(l,U[t])}}})]})})},te=function(e){var t,n=e.blog,r=e.onChange,a=(0,i.useMemo)((function(){return(0,f.Z)(r,500)}),[r]),s="Article"===n.type?n:void 0,l=Array.isArray(null===s||void 0===s?void 0:s.body)?null===s||void 0===s?void 0:s.body:[{type:"paragraph",children:[{text:null!==(t=null===s||void 0===s?void 0:s.body)&&void 0!==t?t:""}]}],c={alias:m.Ib.BlogMainContentDriveId.toString(),type:m.Hm.DriveType.toString()};return(0,w.jsxs)(w.Fragment,{children:[(0,w.jsxs)("div",{className:"mb-5",children:[(0,w.jsx)("label",{className:"mb-2 block",htmlFor:"blog_caption",children:(0,o.t)("Caption")}),(0,w.jsx)(v.Z,{id:"blog_caption",name:"caption",defaultValue:n.caption,onBlur:a})]}),(0,w.jsxs)("div",{className:"mb-5",children:[(0,w.jsx)("label",{className:"mb-2 block",htmlFor:"blog_image",children:(0,o.t)("Primary image")}),(0,w.jsx)(h.Z,{id:"blog_image",name:"primaryImageFileId",defaultValue:n.primaryImageFileId,onChange:a,targetDrive:c,acl:{requiredSecurityGroup:m.hh.Anonymous}})]}),n.slug?(0,w.jsxs)("div",{className:"mb-5",children:[(0,w.jsx)("label",{className:"mb-2 block",htmlFor:"blog_slug",children:(0,o.t)("Slug [raw & readonly]")}),(0,w.jsx)(v.Z,{id:"blog_slug",name:"slug",defaultValue:n.slug,disabled:!0})]}):null,s?(0,w.jsxs)(w.Fragment,{children:[(0,w.jsxs)("div",{className:"mb-5",children:[(0,w.jsx)("label",{className:"mb-2 block",htmlFor:"blog_abstract",children:(0,o.t)("Abstract")}),(0,w.jsx)(g.Z,{id:"blog_abstract",name:"abstract",defaultValue:s.abstract,onChange:a})]}),(0,w.jsx)(ee,{defaultValue:l,placeholder:(0,o.t)("Start writing..."),onChange:function(e){r({target:{name:"body",value:e}})}})]}):null]})},ne=function(e){var t=e.saveBlog,n=(0,i.useState)({acl:{requiredSecurityGroup:m.hh.Anonymous},publishTargets:[],content:{id:"",channelId:"",caption:"",dateUnixTime:(new Date).getTime(),type:"Article",abstract:"",headerImageFileId:"",primaryImageFileId:"",body:""}}),r=(0,a.Z)(n,2),s=r[0],l=r[1];return(0,w.jsx)(te,{blog:s.content,onChange:function(e){var n=(0,d.Z)({},s);n.content[e.target.name]!==e.target.value&&(-1!==Object.keys(n.content).indexOf(e.target.name)&&(n.content[e.target.name]=e.target.value),n.content.caption.length&&(l(n),t(n)))}})},re=function(e){var t=e.blog,n=e.saveBlog,r=(0,d.Z)({},t);return(0,w.jsx)(te,{blog:t.content,onChange:function(e){r.content[e.target.name]=e.target.value,r.content.caption.length&&n(r)}})},ae=n(9072),ie=n(3433),se=n(4164),le=n(3412),oe=function(e){return(0,w.jsx)("input",(0,d.Z)((0,d.Z)({},e),{},{type:"checkbox",className:"h-4 w-4 rounded border-gray-300 bg-gray-100 text-blue-600 focus:ring-2 focus:ring-blue-500 dark:border-gray-600 dark:bg-gray-700 dark:ring-offset-gray-800 dark:focus:ring-blue-600 ".concat(e.className)}))},ce=n(8395),ue=function(e){var t=e.channel,n=e.blogFile,r=(0,c.Z)().unpublish,a=r.mutate,i=r.status;return(0,w.jsxs)("div",{className:"mb-2 flex flex-row",children:[(0,w.jsx)(ae.Z,{icon:"trash",title:(0,o.t)("Unpublish from this channel"),className:"mr-2 px-2 py-1",type:"remove",onClick:function(){return a({blogFile:n,channelId:t.channelId})},state:i}),(0,w.jsx)("p",{className:"my-auto",children:t.name})]},t.channelId)},de=function(e){var t=e.title,n=e.confirmText,r=e.isOpen,s=e.channels,l=e.blogFile,u=e.onConfirm,m=e.onCancel,f=(0,le.Z)("modal-container"),h=(0,c.Z)().publish,v=h.mutate,g=h.status,x=(0,i.useState)(null),p=(0,a.Z)(x,2),b=p[0],j=p[1];if(!r)return null;var C=l.publishTargets.map((function(e){return s.find((function(t){return t.channelId===e.channelId}))})),y=s.reduce((function(e,t){return C.find((function(e){return e.channelId===t.channelId}))?e:[].concat((0,ie.Z)(e),[t])}),[]),Z=(0,w.jsx)(ce.Z,{title:t,onClose:m,children:(0,w.jsxs)(w.Fragment,{children:[(0,w.jsxs)("div",{children:[C.length?(0,w.jsx)(w.Fragment,{children:(0,w.jsxs)("div",{className:"-mx-8 mb-5 py-10 px-8",children:[(0,w.jsx)("h2",{className:"mb-2 text-lg",children:"Already published to:"}),C.map((function(e){return(0,w.jsx)(ue,{channel:e,blogFile:l},e.channelId)}))]})}):null,y.length?(0,w.jsxs)(w.Fragment,{children:[C.length?(0,w.jsx)("h2",{className:"mb-2 text-lg",children:"Other Channels:"}):null,y.map((function(e){return(0,w.jsxs)("div",{className:"mb-2",children:[(0,w.jsx)(oe,{value:e.channelId,id:e.channelId,onChange:function(e){var t=(0,d.Z)({},b);t[e.target.value]=e.target.checked,j(t)}}),(0,w.jsx)("label",{htmlFor:e.channelId,className:"ml-2",children:e.name})]},e.channelId)}))]}):null]}),(0,w.jsxs)("div",{className:"-m-2 flex flex-row-reverse",children:[(0,w.jsx)(ae.Z,{className:"m-2",state:g,onClick:function(){var e=s.reduce((function(e,t){if(b&&b[t.channelId]){var n,r,a=l.publishTargets.find((function(e){return e.channelId===t.channelId}));return[].concat((0,ie.Z)(e),[{acl:t.acl,channelId:t.channelId,fileId:null!==(n=null===a||void 0===a?void 0:a.fileId)&&void 0!==n?n:void 0,lastPublishTime:null!==(r=null===a||void 0===a?void 0:a.lastPublishTime)&&void 0!==r?r:void 0}])}return e}),[]);v({blogFile:l,publishTargets:[].concat((0,ie.Z)(l.publishTargets),(0,ie.Z)(e))},{onSuccess:function(){u()}})},children:null!==n&&void 0!==n?n:"Publish"}),(0,w.jsx)(ae.Z,{className:"m-2",type:"secondary",onClick:m,children:(0,o.t)("Cancel")})]})]})});return(0,se.createPortal)(Z,f)},me=n(3004),fe=n(8739),he=function(){var e,t,n,r=(0,s.s0)(),d=(0,s.UO)().blogKey,m=(0,u.Z)().fetch,f=m.data,h=m.isLoading,v=(0,i.useState)(!1),g=(0,a.Z)(v,2),x=g[0],p=g[1],b=(0,c.Z)({blogSlug:d}),j=b.fetch,C=j.data,y=j.isLoading,Z=b.save,N=Z.mutate,k=Z.status,I=b.remove.mutate,M=function(e){N(e,{onSuccess:function(t){var n;if(!C||d!==(0,l.V)(e.content.caption)&&d!==(null===(n=e.fileId)||void 0===n?void 0:n.toString())){var a,i=null!==(a=e.content.caption)&&void 0!==a&&a.length?(0,l.V)(e.content.caption):t.toString(),s=window.location.pathname.split("/");s.pop(),s&&i&&r("".concat(s.join("/"),"/").concat(i))}}})};return!f||h||y?(0,w.jsx)(fe.Z,{}):(0,w.jsxs)(w.Fragment,{children:[(0,w.jsxs)("section",{children:[(0,w.jsx)(me.Z,{title:C?(0,w.jsx)(w.Fragment,{children:C.content.caption}):(0,o.t)("New blog"),actions:(0,w.jsxs)(w.Fragment,{children:[(0,w.jsx)(ae.Z,{type:"remove",className:"m-2",icon:"trash",confirmOptions:{title:"".concat((0,o.t)("Remove")," ").concat(null!==(e=null===C||void 0===C?void 0:C.content.caption)&&void 0!==e?e:""),buttonText:"Permanently remove",body:(0,o.t)("Are you sure you want to remove this blog? This action cannot be undone.")},onClick:function(){return I({fileId:C.fileId,slug:C.content.slug},{onSuccess:function(){var e=window.location.pathname.split("/");e.pop(),e&&r("".concat(e.join("/")))}})}}),(0,w.jsx)(ae.Z,{type:"primary",className:"m-2",onClick:function(){return p(!0)},children:(0,o.t)("Publish")})]}),breadCrumbs:[{href:"/owner/blog",title:"Blog"},{title:null!==(t=null===(n=C.content)||void 0===n?void 0:n.caption)&&void 0!==t?t:""}],saveStatus:k}),(0,w.jsx)("div",{className:"md:mx-auto md:max-w-[40rem]",children:C?(0,w.jsx)(re,{blog:C,saveBlog:M}):(0,w.jsx)(ne,{saveBlog:M})})]}),C?(0,w.jsx)(w.Fragment,{children:(0,w.jsx)(de,{isOpen:x,onCancel:function(){return p(!1)},onConfirm:function(){return p(!1)},blogFile:C,channels:f,title:"".concat((0,o.t)("Publish"),": ").concat(C.content.caption)})}):null]})}},777:function(e,t,n){var r=n(4165),a=n(1413),i=n(5861),s=n(7408),l=n(6752),o=n(3990),c=n(6117);t.Z=function(){var e=(0,c.Z)().getSharedSecret,t=new l.KU({api:l.Ii.Owner,sharedSecret:e()}),n=function(){var e=(0,i.Z)((0,r.Z)().mark((function e(){var n;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,t.blogDefinitionProvider.getChannelDefinitions();case 2:return n=e.sent,e.abrupt("return",n.map((function(e){return(0,a.Z)((0,a.Z)({},e),{},{slug:(0,o.V)(e.name)})})));case 4:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}();return{fetch:(0,s.useQuery)(["channels"],(function(){return n()}),{refetchOnWindowFocus:!1})}}},9779:function(e,t,n){var r=n(4165),a=n(5861),i=n(7408),s=n(6752),l=n(6117),o={alias:s.Ib.BlogMainContentDriveId.toString(),type:s.Hm.DriveType.toString()};t.Z=function(e,t){var n=(0,l.Z)().getSharedSecret,c=(0,i.useQueryClient)(),u=new s.KU({api:s.Ii.Owner,sharedSecret:n()}),d=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t,n){return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(void 0!==t&&""!==t){e.next=2;break}return e.abrupt("return");case 2:return e.next=4,u.mediaProvider.getDecryptedImageUrl(null!==n&&void 0!==n?n:o,t);case 4:return e.abrupt("return",e.sent);case 5:case"end":return e.stop()}}),e)})));return function(t,n){return e.apply(this,arguments)}}(),m=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a,i,l,c,d,m;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=t.bytes,a=t.targetDrive,i=void 0===a?o:a,l=t.acl,c=void 0===l?{requiredSecurityGroup:s.hh.Anonymous}:l,d=t.fileId,m=void 0===d?void 0:d,e.next=3,u.mediaProvider.uploadImage(i,void 0,c,n,m);case 3:return e.abrupt("return",e.sent);case 4:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}(),f=function(){var e=(0,a.Z)((0,r.Z)().mark((function e(t){var n,a,i;return(0,r.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return n=t.targetDrive,a=void 0===n?o:n,i=t.fileId,e.next=3,u.mediaProvider.removeImage(i,a);case 3:return e.abrupt("return",e.sent);case 4:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}();return{fetch:(0,i.useQuery)(["image",e,t],(function(){return d(e,t)}),{refetchOnMount:!1,refetchOnWindowFocus:!1,staleTime:1/0}),save:(0,i.useMutation)(m,{onSuccess:function(e,t){var n;t.fileId?c.removeQueries(["image",t.fileId,null!==(n=t.targetDrive)&&void 0!==n?n:o]):c.removeQueries(["image"])}}),remove:(0,i.useMutation)(f,{onSuccess:function(e,t){var n;t.fileId?c.removeQueries(["image",t.fileId,null!==(n=t.targetDrive)&&void 0!==n?n:o]):c.removeQueries(["image"])}})}}}}]);
//# sourceMappingURL=634.48698a91.chunk.js.map