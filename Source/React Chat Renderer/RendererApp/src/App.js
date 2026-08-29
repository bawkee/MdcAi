import './App.css';
import { useState, useEffect, useRef } from 'react';
import CodeHighlighter from './components/highlighter';
import AutoScrollComponent from './components/autoScroll';
import DateTime from './components/dateTime';
import { isElementFullyVisible } from './util';
import { getAuthorLabel, getEffortLabel, getRoleClass } from './messageMeta';
//import initialData from './sample1.json';

function App() {
  const [data, setData] = useState(null);
  const [selectedChat, setSelectedChat] = useState(null);
  const [autoScroll, setAutoScroll] = useState(true);
  const chatItemRefs = useRef({});
  const scrolledDownRef = useRef(true);
  // Mirror of selectedChat kept in a ref so the (once-registered) webview message
  // listener never sees a stale selection value.
  const selectedChatRef = useRef(null);

  useEffect(() => { selectedChatRef.current = selectedChat; }, [selectedChat]);

  useEffect(() => {
    const handleMessage = (e) => {
      let obj;

      if (typeof e.data === 'string') {
        //console.log(`String received: ${e.data}`);
        obj = JSON.parse(e.data);
      } else {
        //console.log(`Obj received: ${JSON.stringify(e.data)}`);
        obj = e.data;
      }

      if (obj.Name === 'SetMessages')
        setData(obj.Data);
      else if (obj.Name === 'HideCaret')
        setTimeout(hideCaret, 1000);
      else if (obj.Name === 'SetSelection')
        onSelectedChat(obj.Data, false);
    }

    if (window.chrome.webview)
      window.chrome.webview.addEventListener('message', handleMessage);

    return () => {
      if (window.chrome.webview)
        window.chrome.webview.removeEventListener('message', handleMessage);
    };
  }, []);

  const hideCaret = () => {
    document
      .querySelectorAll('#caret')
      .forEach(element => element.remove());
  }

  const onSelectedChat = (index, forward = true) => {
    if (index === selectedChatRef.current)
      return;

    setSelectedChat(index);

    if (window.chrome.webview && forward) {
      const selReq = {
        Name: "SetSelection",
        Data: index
      };

      window.chrome.webview.postMessage(selReq);
    }

    scrollToMessage(index);
  }

  const scrollToMessage = (index) => {
    const chatItem = chatItemRefs.current[index];

    if (chatItem) {
      if (!isElementFullyVisible(chatItem)) {
        setAutoScroll(false);
        chatItem.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      }
    }
  };

  useEffect(() => {
    const handleScroll = () => {
      let scrolledDownNew = (window.innerHeight + window.scrollY) >= document.documentElement.scrollHeight - 5;
      let scrolledDownChanged = scrolledDownRef.current !== scrolledDownNew;
      scrolledDownRef.current = scrolledDownNew;

      if (window.chrome.webview && scrolledDownChanged) {
        const scrollDownMsg = {
          Name: "IsScrollToBottom",
          Data: scrolledDownNew
        };
        window.chrome.webview.postMessage(scrollDownMsg);
      }
    };

    window.addEventListener('scroll', handleScroll);
    return () => {
      window.removeEventListener('scroll', handleScroll);
    };
  }, []);


  return (
    <div className='App'>
      <AutoScrollComponent
        autoScroll={autoScroll}
        setAutoScroll={setAutoScroll}>
        <div className='chat-list'>
          {(data?.Messages || []).map((item, index) => (
            <div
              ref={el => chatItemRefs.current[index] = el}
              key={index}
              onClick={() => onSelectedChat(index)}
              className={`chat-item ${getRoleClass(item.Role)} ${selectedChat === index ? 'active' : ''}`}>
              <div className='chat-item-marque' />
              <div className='chat-item-info'>
                <span className='chat-item-info-role'>
                  {getAuthorLabel(item)}
                </span>
                {getEffortLabel(item) && (
                  <span className='chat-item-info-effort'>
                    {getEffortLabel(item)}
                  </span>
                )}
                <span className='chat-item-info-createdts'>
                  sent <DateTime date={new Date(item.CreatedTs)} />
                </span>
                {item.VersionCount > 1 && (
                  <span
                    className='chat-item-info-version'
                    title='Version of the edited message'>{item.Version} / {item.VersionCount}</span>
                )}
              </div>
              <div className='chat-item-content'>
                <CodeHighlighter code={item.Content} />
              </div>
            </div>
          ))}
        </div>
      </AutoScrollComponent>
    </div>
  );
}

const readyPing = {
  Name: "Ready"
};

if (window.chrome.webview)
  window.chrome.webview.postMessage(readyPing);

export default App;