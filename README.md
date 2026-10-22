# Online Betting Data Capture and Analytics System

## Scenario
In the dynamic world of online betting, capturing and analyzing data is paramount for business success. Every time a player initiates a spin in a casino game, it's essential to capture and store relevant data. This project aims to develop an API and service capable of receiving, storing, and retrieving player casino data.

## Purpose of the Assessment
Your solution should showcase your experience, standards, and passion for software development. Submit work that reflects the quality we can expect from you if you join our team.

The tester is provided. You must implement the API, consumer service, and database schema, including the database creation script and setup instructions.

Good luck and have fun.

## Pre-Requisites
- **Project Boilerplate:** Download and extract [ot-backend-assessment.zip](../ot-backend-assessment.zip). Open the extracted `ot-backend-assessment` folder containing `src`, `test`, and this README. Run the commands below from that folder.

- **Message Bus:** RabbitMQ, Kafka, Redis, etc.

    Document your message bus setup in the submission notes. For example, describe how to run a local RabbitMQ instance on `localhost` with credentials `guest/guest`.

    > [Run RabbitMQ Windows Service](https://www.rabbitmq.com/docs/install-windows#installer) or
 
    > [Docker Compose Rabbit Image](docker/RabbitMQ/docker-up.bat)
- **SQL Server:** Setup a local [SQL Server Developer Edition](https://www.microsoft.com/en-za/sql-server/sql-server-downloads) or docker compose with the connection string: `"SERVER=localhost; DATABASE=OT_Assessment_DB; Integrated Security=SSPI;"`.


## Requirements Tasks
### 1. .NET API
The API (OT.Assessment.App) must at least include the following endpoints:
- **POST** `api/player/casinowager`: Receives player casino wager events and publishes them to your chosen message bus (RabbitMQ, Kafka, Redis, etc.).
    ```json
    {
      "wagerId": "aa6700eb-1a06-483e-9739-d293dc7a9383",
      "theme": "adventure",
      "provider": "Ergonomic Soft Fish",
      "gameName": "Ergonomic Granite Cheese",
      "transactionId": "410b7161-3473-4d74-85c3-a533d050a9d3",
      "brandId": "8a2016f8-c4c4-471f-9a9c-337a54664650",
      "accountId": "5ac75fec-23e9-27d1-b660-179eee70003d",
      "Username": "Jay.Bernhard67",
      "externalReferenceId": "0267dbca-2760-4a9e-ab42-5ce766fa8ca0",
      "transactionTypeId": "8aaece0c-5d53-4225-a937-adb454c4da31",
      "amount": 38273.974454660885,
      "createdDateTime": "2024-05-04T02:25:05.9906387+02:00",
      "numberOfBets": 3,
      "countryCode": "BS",
      "sessionData": "Central Chile Awesome Cotton Gloves cross-platform Handmade Rubber Shoes portals leading-edge Coordinator Data Producer end-to-end encoding Gorgeous Clothing View Health, Outdoors & Music embrace Metrics Facilitator morph",
      "duration": 1827254
    }
    ```
- **GET** `api/player/{playerId}/casino?pageSize=10&page=1`: Returns a paginated list of the latest casino wagers for a specific player. The `{playerId}` route parameter is the `accountId` supplied in the wager payload.
    ```json
    {
      "data": [
        {
          "wagerId": "aa6700eb-1a06-483e-9739-d293dc7a9383",
          "game": "Ergonomic Granite Cheese",
          "provider": "Ergonomic Soft Fish",
          "amount": 500,
          "createdDate": "2024-09-10T07:25:13.577Z"
        },
        {
          "wagerId": "aa6700eb-1a06-483e-9739-d293dc7a9383",
          "game": "Test",
          "provider": "Test",
          "amount": 250,
          "createdDate": "2024-09-10T07:25:13.577Z"
        },
        //etc
      ],
      "page": 1,
      "pageSize": 10,
      "total": 35,
      "totalPages": 4
    }
    ```
- **GET** `api/player/topSpenders?count=10`: Returns the top {count} players based on total spending. Highest to Lowest.
    ```json
    [
      {
        "accountId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "username": "testUser1",
        "totalAmountSpend": 150
      },
      {
        "accountId": "aa6700eb-5717-4562-b3fc-2c963f66bev7",
        "username": "testUser21",
        "totalAmountSpend": 100
      },
      //etc
    ]
    ```

### 2. .NET Service
This service (OT.Assessment.Consumer) needs to consume messages published to the aforementioned queue and store consumed messages in the SQL database.

### 3. SQL Database
Design the tables, indexes, keys, stored procedures, etc. necessary to store and retrieve the data required above. Follow database best practices and minimise unnecessary duplication. Not all payload fields need to be stored; use your discretion to retain the data needed to meet the requirements.

**DatabaseGenerate.sql**: Populate the file in the root of the directory with the necessary statements to recreate the database.

### 4. Benchmark and Test
Test your application by running the <b>OT.Assessment.Tester</b>.

The tester requires the **.NET 10 SDK** and uses **Bogus** to generate casino wager data and **NBomber** to send it to your API.

1. First, run the API Application and the Consumer Service simultaneously.
2. Edit [test/OT.Assessment.Tester/appsettings.json](test/OT.Assessment.Tester/appsettings.json) and set `Api:EndpointUri` to the full URL of your running API's casino wager POST endpoint, including its scheme, port, and route. For the required assessment route, for example:
   ```json
   {
     "Api": {
       "EndpointUri": "http://localhost:5000/api/player/casinowager"
     }
   }
   ```
   The supplied setting is `http://localhost:5000/api`. Replace it with your actual wager endpoint before running the tester. The tester posts directly to `Api:EndpointUri` as configured; it does not append `/player/casinowager` or any other route. Use an absolute `http://` or `https://` URI without a fragment (`#...`); query strings are supported if needed. For local HTTPS, ensure your development certificate is trusted (`dotnet dev-certs https --trust`).
3. From the extracted assessment root directory (the folder containing `test`), run:
   ```powershell
   dotnet run --project .\test\OT.Assessment.Tester\OT.Assessment.Tester.csproj
   ```
   The settings file is copied to the build and publish output and read from alongside the tester executable. The tester validates `Api:EndpointUri` before starting the load test and prints the target endpoint.
4. The tester posts Bogus-generated casino wagers to your configured API, populating the queue with relevant data. Concurrently, your consumer application should process incoming messages from the queue and store them within your database.
5. Debug, optimise and ensure your application can handle the load by storing all the messages.
6. Ensure both GET endpoints return data during the load test and after the load test.

**Load-test success:** Every wager submitted by the tester must eventually be stored in the database. The queue may continue draining after the tester finishes; keep the consumer running until processing is complete. Check for failed submissions and processing errors, and reconcile the submitted wagers with the stored records to demonstrate that no wagers were lost. Both GET endpoints must remain usable during ingestion and return correct results once processing is complete. Record the test results, any failures, and the total time taken to store all wagers in your submission notes.

### 5. BONUS
Surprise us! Add some things to your project you think are valuable additions. This will help you stick out from the rest of the candidates so try be creative.

### 6. Notes
Document your design choices and reasoning in [Notes.md](Notes.md). Include setup instructions, challenges encountered, and suggested improvements for a production-ready solution.

## Scoring Criteria
- Completion of all requirement tasks as specified above.
- Architecture and project structure.
- Naming conventions for variables, projects, tables, and stored procedures.
- Adherence to SOLID principles.
- Readability and maintainability of the code.
- Efficient database design.
- Efficiently consuming all the data from the tester application as quickly as possible.
- Bonus features or additions that demonstrate passion, innovation, and quality.
- Proficiency and adherence to best practices for .NET projects, background services, message queues, RESTful APIs, Git, and SQL Server.

## FAQ
- Can I modify the suggested solutions, projects, and classes? <b> Yes, it is encouraged but ensure all pre-requisites and tasks are completed as stated above.</b>
- What technologies should I use? <b>Feel free to use any packages you're comfortable with in the .NET ecosystem.</b>
- How do I test my application? <b>Run the tester application to simulate sending casino game data to your API. Ensure both GET routes return the correct data</b>
- How is my application tested and assessed? <b>We'll generate the database from your schema script, start the API and consumer, run the tester application, and evaluate against the scoring criteria</b>
- Can I use AI? <b>We encourage you to complete the assessment yourself and limit AI assistance to routine boilerplate tasks. Your submission must demonstrate your own engineering judgement. You are responsible for the correctness and quality of all submitted code and must be able to explain your implementation and design decisions in a follow-up interview.</b>
- Need clarification? <b>Compile a list of questions and send them to your OT representative for prompt assistance</b>
- What's next after completing and testing the solution? <b>Ensure your database schema script and submission notes are complete. Commit your code to a public GitHub repository and share the link with your OT representative. Document any further enhancements needed for production readiness.</b>

Try to remember and briefly document the decisions you made and why you made them. We may ask you in a follow-up interview.