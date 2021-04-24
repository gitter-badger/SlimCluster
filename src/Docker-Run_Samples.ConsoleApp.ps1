docker build --build-arg SERVICE=SlimCluster.Samples.ConsoleApp -t zarusz/consoleapp:latest .

docker run -it zarusz/consoleapp:latest
# In case you need to debug why it's not starting:
# docker run -it zarusz/consoleapp:latest sh
